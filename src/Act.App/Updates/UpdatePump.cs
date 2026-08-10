using Act.App.Desktop;
using Act.App.Hosting;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Updates;

// Everything about *when* to look for a newer ACT and *how far* to go once one exists. The updater
// behind it knows only how to ask Electron; the policy lives here, where it can be tested without a
// desktop.
//
// The toast is governed by `UpdatePolicy` alone and not by the *Desktop notifications* switch: that
// switch says "ping when a task needs you", which this is not, and the policy is already the user's
// control over how loud updates are — `Off` is the answer for someone who wants none of it.
public sealed class UpdatePump(
    IUpdater updater,
    UpdateState state,
    UserSettingsService settings,
    INotifier notifier,
    IClock clock,
    ILogger<UpdatePump> log) : IAsyncDisposable
{
    // Long, because a release is a rare event and the cost of hearing about one six hours late is
    // nothing. The first pass runs at startup, which is when most people would have noticed anyway.
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly BackgroundWork work = new(log);

    private UpdatePolicy known;

    private (string Version, UpdateStage Stage)? announced;

    private bool started;

    private bool downloadRequested;

    public void Start()
    {
        // Browser mode has no installer to replace. Asked here rather than in the composition root
        // so there is one answer to "is this shell updatable" and everything reads it.
        if (!updater.IsSupported)
            return;

        // Guarded because this runs inside the desktop shell's startup, and an update check is the
        // least important thing happening there. `Configure` reaches into Electron, so a bridge that
        // has moved under it throws — and unguarded that took the shell down with it, leaving a
        // packaged ACT stuck on its splash screen. Nothing else here is allowed to cost the window.
        try
        {
            updater.Configure();
        }
        catch (Exception error)
        {
            log.LogError(error, "The updater could not be configured; ACT will not check for updates this run.");

            return;
        }

        started = true;
        known = settings.Updates;

        settings.Changed += OnSettingsChanged;

        work.StartLoop("The update check", Interval, _ => work.RunAsync(PassAsync));
    }

    // The Settings page's *Check now*. Through the same single-flight gate as the loop, so an
    // impatient click during a running check joins it rather than starting a second one.
    public void CheckNow()
    {
        if (started)
            work.Request("An update check", PassAsync);
    }

    // What makes `NotifyOnly` a setting rather than a dead end: told a version exists, the user needs
    // a way to say yes. Re-checks on the way, which is right — the answer may be older than the
    // click, and downloading what is no longer the latest would be worse than asking twice.
    public void DownloadNow()
    {
        if (!started)
            return;

        downloadRequested = true;

        work.Request("An update download", PassAsync);
    }

    // Only when the *update* policy moved. `Changed` fires for every setting there is, and an update
    // check per keystroke in a text box would be a poor way to treat somebody's network.
    private void OnSettingsChanged()
    {
        if (settings.Updates == known)
            return;

        known = settings.Updates;

        work.Request("An update check", PassAsync);
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        // Read and cleared here rather than where it is used, so a pass that returns early does not
        // leave the flag armed for a later one the user never asked for.
        var requested = downloadRequested;

        downloadRequested = false;

        if (settings.Updates is UpdatePolicy.Off)
        {
            announced = null;

            state.Publish(UpdateStatus.Idle);

            return;
        }

        // Already downloaded: there is nothing left to look for until the app restarts into it, and
        // asking again would only offer the version it is holding.
        if (state.Current.Stage is UpdateStage.Ready)
            return;

        state.Publish(state.Current with { Stage = UpdateStage.Checking });

        var check = await updater.CheckAsync(cancellationToken);
        var at = clock.Now;

        if (!check.Completed)
        {
            state.Publish(new UpdateStatus(UpdateStage.Unavailable, CheckedAt: at));

            return;
        }

        if (check.Version is not { } version)
        {
            announced = null;

            state.Publish(new UpdateStatus(UpdateStage.UpToDate, CheckedAt: at));

            return;
        }

        log.LogInformation("Update {Version} is available; running {Current}.", version, AppVersion.Current);

        state.Publish(new UpdateStatus(UpdateStage.Available, version, CheckedAt: at));

        if (settings.Updates is UpdatePolicy.NotifyOnly && !requested)
        {
            await AnnounceAsync(
                version,
                UpdateStage.Available,
                Strings.Notify_UpdateAvailable,
                Strings.Notify_UpdateAvailable_Body);

            return;
        }

        await DownloadAsync(version, at, cancellationToken);
    }

    private async Task DownloadAsync(string version, DateTimeOffset at, CancellationToken cancellationToken)
    {
        state.Publish(new UpdateStatus(UpdateStage.Downloading, version, 0, at));

        // Not a `Publish` of its own: a report can outlive the download it belongs to, and only the
        // state can refuse a late one without a gap between deciding and writing.
        var progress = new Progress<int>(percent => state.PublishProgress(version, percent));

        if (await updater.DownloadAsync(progress, cancellationToken))
        {
            state.Publish(new UpdateStatus(UpdateStage.Ready, version, 100, at));

            await AnnounceAsync(
                version,
                UpdateStage.Ready,
                Strings.Notify_UpdateReady,
                Strings.Notify_UpdateReady_Body);

            return;
        }

        // Back to `Available` rather than `Unavailable`: the version is real and the check that
        // found it succeeded — only the fetch failed, and the next pass will try it again.
        state.Publish(new UpdateStatus(UpdateStage.Available, version, CheckedAt: at));
    }

    // Once per version per stage. A pass runs every six hours and on every policy change, and a
    // toast repeating "1.2.0 is ready" four times a day is how a useful notification becomes one
    // people learn to dismiss without reading.
    private async Task AnnounceAsync(string version, UpdateStage stage, string title, string body)
    {
        if (announced == (version, stage))
            return;

        announced = (version, stage);

        try
        {
            await notifier.ShowAsync(new DesktopNotification(null, title, Text.Format(body, version)));
        }
        catch (Exception error)
        {
            log.LogError(error, "Could not show the notification for update {Version}.", version);
        }
    }

    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;

        await work.DisposeAsync();
    }
}
