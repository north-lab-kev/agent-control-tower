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
// The toast is governed by `AutoUpdate` alone and not by the *Desktop notifications* switch: that
// switch says "ping when a task needs you", which this is not, and the toggle is already the user's
// control over how loud updates are — off is the answer for someone who wants none of it.
//
// **One toast, and only for a version already on disk.** Announcing availability as well was what the
// retired middle position needed; with the toggle, a version found while it is on is downloaded in the
// same pass, so "1.2.0 exists" would be a notification whose own successor is seconds behind it.
//
// **It keeps checking after a version is ready**, which it did not always. Nothing installs on the way
// out any more, so a downloaded update can sit unanswered for days — and a pump that stopped looking
// would hold 1.2.0 out while 1.3.0 shipped, then take two restarts to arrive at it. The offer is
// swapped for a newer version only once that one is on disk, so the button never points at a file the
// updater has already discarded. Checking at the click instead was rejected for exactly that: see
// `docs/design-notes.md`, "A silent install cannot tell you it finished".
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

    private bool known;

    private string? announced;

    private bool started;

    private bool downloadRequested;

    private bool checkRequested;

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
        known = settings.AutoUpdate;

        settings.Changed += OnSettingsChanged;

        work.StartLoop("The update check", Interval, _ => work.RunAsync(PassAsync));
    }

    // The Settings page's *Check now*. Through the same single-flight gate as the loop, so an
    // impatient click during a running check joins it rather than starting a second one.
    //
    // Marked as asked-for, because with the toggle off the pass would otherwise bail before reaching
    // the network and the button would sit there doing nothing — which is what it did.
    public void CheckNow()
    {
        if (!started)
            return;

        checkRequested = true;

        work.Request("An update check", PassAsync);
    }

    // What keeps the toggle's off position from being a dead end: shown a version by a manual check,
    // the user needs a way to say yes. Re-checks on the way, which is right — the answer may be older
    // than the click, and downloading what is no longer the latest would be worse than asking twice.
    public void DownloadNow()
    {
        if (!started)
            return;

        downloadRequested = true;

        work.Request("An update download", PassAsync);
    }

    // Only when the *update* toggle moved. `Changed` fires for every setting there is, and an update
    // check per keystroke in a text box would be a poor way to treat somebody's network.
    private void OnSettingsChanged()
    {
        if (settings.AutoUpdate == known)
            return;

        known = settings.AutoUpdate;

        work.Request("An update check", PassAsync);
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        // Read and cleared here rather than where they are used, so a pass that returns early does
        // not leave a flag armed for a later one the user never asked for.
        var requested = downloadRequested;
        var asked = checkRequested;

        downloadRequested = false;
        checkRequested = false;

        // **A downloaded version is never taken away by a pass**, only replaced once something better
        // is on disk. It was the user's to install whenever they chose the moment it finished
        // downloading, and every one of the answers below would otherwise withdraw the offer for a
        // reason that has nothing to do with the file: the toggle going off, a machine that has gone
        // offline, a release pulled from the feed. Even *Checking* is withheld — a *Restart and install*
        // button that blinked out every six hours would be worse than one that never noticed 1.3.0.
        var downloaded = state.Current.Stage is UpdateStage.Ready ? state.Current.Version : null;

        // Only the passes ACT started itself stop here. A click came from somebody looking at the
        // page, and refusing that is what made *Check now* a button that did nothing while off.
        if (!settings.AutoUpdate && !asked && !requested)
        {
            if (downloaded is not null)
                return;

            announced = null;

            state.Publish(UpdateStatus.Idle);

            return;
        }

        if (downloaded is null)
            state.Publish(state.Current with { Stage = UpdateStage.Checking });

        var check = await updater.CheckAsync(cancellationToken);
        var at = clock.Now;

        if (!check.Completed)
        {
            if (downloaded is null)
                state.Publish(new UpdateStatus(UpdateStage.Unavailable, CheckedAt: at));

            return;
        }

        if (check.Version is not { } version)
        {
            if (downloaded is null)
            {
                announced = null;

                state.Publish(new UpdateStatus(UpdateStage.UpToDate, CheckedAt: at));
            }

            return;
        }

        // The version already on disk, named again six hours later. Nothing to do and nothing to say:
        // the page is already showing it and the toast has already been shown once.
        if (version == downloaded)
            return;

        log.LogInformation("Update {Version} is available; running {Current}.", version, AppVersion.Current);

        state.Publish(new UpdateStatus(UpdateStage.Available, version, CheckedAt: at));

        // With the toggle off the click asked what is out there, not for it to be fetched — and no
        // toast, because the answer is landing on the page the click came from. *Download* is what
        // says yes, and it arrives here as `requested`.
        if (!settings.AutoUpdate && !requested)
            return;

        await DownloadAsync(version, at, cancellationToken);
    }

    private async Task DownloadAsync(string version, DateTimeOffset at, CancellationToken cancellationToken)
    {
        state.Publish(new UpdateStatus(UpdateStage.Downloading, version, 0, at));

        // Not a `Publish` of its own: a report can outlive the download it belongs to, and only the
        // state can refuse a late one without a gap between deciding and writing.
        var progress = new Progress<int>(percent => state.PublishProgress(version, percent));

        if (await updater.DownloadAsync(version, progress, cancellationToken))
        {
            state.Publish(new UpdateStatus(UpdateStage.Ready, version, 100, at));

            await AnnounceAsync(version);

            return;
        }

        // Back to `Available` rather than `Unavailable`: the version is real and the check that
        // found it succeeded — only the fetch failed, and the next pass will try it again.
        state.Publish(new UpdateStatus(UpdateStage.Available, version, CheckedAt: at));
    }

    // Once per version. A pass runs every six hours and on every toggle change, and a toast repeating
    // "1.2.0 is ready" four times a day is how a useful notification becomes one people learn to
    // dismiss without reading. Cleared when a version stops being on offer, so the next one is news
    // again.
    private async Task AnnounceAsync(string version)
    {
        if (announced == version)
            return;

        announced = version;

        try
        {
            await notifier.ShowAsync(new DesktopNotification(
                null,
                Strings.Notify_UpdateReady,
                Text.Format(Strings.Notify_UpdateReady_Body, version)));
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
