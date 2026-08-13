using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Act.App.Desktop;

// `electron-updater`, reached through Electron.NET's bridge, with three of its habits corrected.
//
// **Pre-releases are forced off.** `AppUpdater`'s constructor runs
// `allowPrerelease = hasPrereleaseComponents(currentVersion)`, which quietly overrides its own
// `false` default — so a `0.1.0-beta.1` install tracks betas unless told otherwise, and the setting
// that says otherwise has to be written on every run. With it off a pre-release version named in the
// feed is refused, and the feed is not offering one anyway: `latest.yml` is served from the
// `releases/latest/download` route, which resolves to the newest *stable* release, and the release
// job attaches the file to stable versions only. So a beta simply waits for the stable that
// supersedes it. `AllowDowngrade` stays off so it is never walked backwards to an older stable.
//
// **Nothing here throws.** Until the repository is public every check answers 404 —
// `GenericProvider.getLatestVersion` turns that into `ERR_UPDATER_CHANNEL_FILE_NOT_FOUND`, which
// arrives as an `error` event, not as a rejected check — and offline is forever a possibility. Both
// are ordinary and both must look like "nothing new" rather than an error the user has to dismiss.
// An unpacked development build is quieter still — `isUpdaterActive` returns false there and the
// check resolves with nothing.
//
// **Every call is bounded.** The bridge's `downloadUpdate` handler awaits with no `.catch`
// (`.electron/api/autoUpdater.js`), so a failed download never emits its completion and the task
// waiting on it would hang for the life of the process.
public sealed class ElectronUpdater(ILogger<ElectronUpdater> log) : IUpdater
{
    // Long, because this covers a real download on a bad connection, not a round trip. It is a
    // backstop against the missing `.catch` above rather than a deadline anyone should reach.
    private static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan CheckLimit = TimeSpan.FromMinutes(2);

    // `update-available` is emitted before the check's own completion, but that ordering belongs to
    // the Electron process, and nothing in the bridge promises the two arrive at this end in the
    // same order. So the completion is taken as "stop waiting long", not "stop waiting".
    private static readonly TimeSpan EventGrace = TimeSpan.FromSeconds(2);

    private readonly Lock gate = new();

    private TaskCompletionSource<UpdateCheck>? checking;

    private TaskCompletionSource<bool>? downloading;

    private bool configured;

    public bool IsSupported => true;

    public bool IsReady { get; private set; }

    public void Configure()
    {
        if (configured)
            return;

        configured = true;

        Electron.AutoUpdater.AllowPrerelease = false;
        Electron.AutoUpdater.AllowDowngrade = false;

        // The pump decides when to download, not the updater — the toggle's off position would
        // otherwise fetch the thing it promised not to fetch.
        Electron.AutoUpdater.AutoDownload = false;

        // Off, because *Restart and install* is the only thing allowed to apply an update. Left on,
        // every route out of the app installed one — the tray's *Exit*, closing the last window with
        // close-to-tray off, a shutdown — so somebody who never clicked the button still came back to
        // a version they had not agreed to, and the click was decoration. The installer stays in
        // electron-updater's pending cache instead, for as many runs as it takes — see
        // `docs/design-notes.md`, "A silent install cannot tell you it finished".
        Electron.AutoUpdater.AutoInstallOnAppQuit = false;

        Electron.AutoUpdater.OnUpdateAvailable += info => Settle(UpdateCheck.Found(info?.Version));
        Electron.AutoUpdater.OnUpdateNotAvailable += _ => Settle(UpdateCheck.UpToDate);

        Electron.AutoUpdater.OnUpdateDownloaded += info =>
        {
            IsReady = true;

            log.LogInformation("Update {Version} downloaded and ready to install.", info?.Version);

            TaskCompletionSource<bool>? waiting;

            lock (gate)
                waiting = downloading;

            waiting?.TrySetResult(true);
        };

        // Reported at information rather than error: the expected value of this event, for as long
        // as the repository is private, is a 404 on every pass.
        Electron.AutoUpdater.OnError += message =>
        {
            log.LogInformation("The update check did not complete: {Message}", message);

            lock (gate)
            {
                checking?.TrySetResult(UpdateCheck.Failed);
                downloading?.TrySetResult(false);
            }
        };
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var settled = new TaskCompletionSource<UpdateCheck>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (gate)
            checking = settled;

        try
        {
            var check = Electron.AutoUpdater.CheckForUpdatesAsync();

            // Both are awaited because either alone is insufficient: an unpacked build resolves the
            // check with nothing and fires no event at all, so waiting on the event would burn the
            // whole limit every pass; and the check's result drops `isUpdateAvailable` on its way
            // through the bridge, so it cannot answer the question by itself.
            await Task.WhenAny(settled.Task, check, Task.Delay(CheckLimit, cancellationToken));

            if (!settled.Task.IsCompleted && check.IsCompleted)
                await Task.WhenAny(settled.Task, Task.Delay(EventGrace, cancellationToken));

            return settled.Task.IsCompletedSuccessfully ? settled.Task.Result : UpdateCheck.Failed;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogInformation(error, "The update check failed.");

            return UpdateCheck.Failed;
        }
        finally
        {
            lock (gate)
                checking = null;
        }
    }

    public async Task<bool> DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        if (IsReady)
            return true;

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Report(ProgressInfo info) => progress.Report(Percent(info));

        lock (gate)
            downloading = finished;

        Electron.AutoUpdater.OnDownloadProgress += Report;

        try
        {
            var download = Electron.AutoUpdater.DownloadUpdateAsync();

            await Task.WhenAny(finished.Task, download, Task.Delay(DownloadLimit, cancellationToken));

            if (!IsReady)
                log.LogInformation("The update download did not complete; it will be retried on the next pass.");

            return IsReady;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogInformation(error, "The update download failed.");

            return false;
        }
        finally
        {
            Electron.AutoUpdater.OnDownloadProgress -= Report;

            lock (gate)
                downloading = null;
        }
    }

    // Force-run, which is the whole point: this one was asked for, so ACT owes an answer about when
    // it finished, and coming back is the only answer it can give.
    //
    // Silent is not a free choice here. `nsis.oneClick` is `false`, so the visible installer is the
    // assisted wizard — Next, install directory, Install, Finish — and electron-updater only ever
    // passes `/S` or nothing (`NsisUpdater.doInstall`), never a middle setting. `oneClick` is a
    // build-time choice baked into the installer, so it cannot be one thing for a manual install and
    // another for this one.
    public void InstallAndRestart() => Electron.AutoUpdater.QuitAndInstall(isSilent: true, isForceRunAfter: true);

    private static int Percent(ProgressInfo info) => Math.Clamp((int)Math.Round(info.Percent), 0, 100);

    private void Settle(UpdateCheck check)
    {
        TaskCompletionSource<UpdateCheck>? waiting;

        lock (gate)
            waiting = checking;

        waiting?.TrySetResult(check);
    }
}
