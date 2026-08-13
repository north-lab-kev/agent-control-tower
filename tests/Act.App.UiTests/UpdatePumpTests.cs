using Act.App.Desktop;
using Act.App.Settings;
using Act.App.Updates;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The deciding half of auto-update: when ACT asks, how far it goes once told, and what it says about
// it. The Electron half is `ElectronUpdater` and is not testable here — which is exactly why none of
// the deciding lives there.
public class UpdatePumpTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsStore store = new();

    private readonly FrozenClock clock = new(Now);

    private readonly UpdateState state = new();

    private readonly RecordingNotifier notifier = new();

    private readonly FakeUpdater updater = new();

    [Fact]
    public async Task Starting_checks_without_waiting_for_the_first_interval()
    {
        updater.Answer = UpdateCheck.UpToDate;

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.UpToDate);

        updater.Checks.Should().Be(1);
        updater.Configured.Should().Be(1);
    }

    // The whole point of the position. Not "checks and discards the answer" — nothing may reach the
    // network on ACT's own initiative, and a count is the only way to say so. What a click may do is
    // below.
    [Fact]
    public async Task The_toggle_off_never_asks_anything_on_its_own()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);

        await using var pump = Pump(settings);

        pump.Start();

        await Task.Delay(100);

        updater.Checks.Should().Be(0);
        state.Current.Stage.Should().Be(UpdateStage.Idle);
        notifier.Shown.Should().BeEmpty();
    }

    // The bug this shipped with: *Check now* was visible and live with the toggle off, and the pass it
    // started bailed before the network, so the button did nothing and the page went on saying it was
    // not checking. A click is not ACT's own initiative.
    [Fact]
    public async Task The_toggle_off_still_checks_when_the_user_asks()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        state.Current.Version.Should().Be("1.2.0");
    }

    // Asked what is out there, not for it to be fetched — and no toast, because the answer lands on
    // the page the click came from.
    [Fact]
    public async Task A_manual_check_neither_downloads_nor_announces()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.Available);
        await Task.Delay(100);

        updater.Downloads.Should().Be(0);
        notifier.Shown.Should().BeEmpty();
    }

    // Otherwise a manual check names a version and leaves no way to have it, which is the dead end the
    // toggle would be if collapsing the three positions had dropped *Download* with them.
    [Fact]
    public async Task The_toggle_off_downloads_what_a_manual_check_found_when_asked()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        pump.DownloadNow();

        await Until(() => state.Current.Stage is UpdateStage.Ready);

        updater.Downloads.Should().Be(1);
    }

    // Having clicked once must not turn the toggle on behind the user's back: the flag is spent on
    // the pass that honoured it, and the interval pass that follows goes back to asking nothing.
    [Fact]
    public async Task Clicking_check_once_does_not_arm_the_next_pass()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.UpToDate;

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.UpToDate);

        // Nothing else may reach the network: the toggle moving is the only other trigger, and these
        // are not it.
        settings.SetKeepAwake(false);
        settings.SetMaxConcurrent(3);

        await Task.Delay(100);

        updater.Checks.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_runs_at_all_where_there_is_no_installer_to_replace()
    {
        updater.IsSupported = false;

        await using var pump = Pump();

        pump.Start();

        await Task.Delay(100);

        updater.Configured.Should().Be(0);
        updater.Checks.Should().Be(0);
    }

    // The pump starts from inside the desktop shell's startup, so an exception escaping `Start` is
    // not "updates are broken" — it is a packaged ACT that never gets past its splash screen. That
    // happened: `ElectronUpdater.Configure` threw a `NullReferenceException` because it ran before
    // Electron's bridge existed. The ordering is fixed where it is started; this is the guarantee
    // that a bridge moving again costs the feature and not the window.
    [Fact]
    public async Task A_updater_that_cannot_be_configured_does_not_take_the_shell_down()
    {
        updater.ConfigureThrows = new NullReferenceException("the bridge is not up yet");

        await using var pump = Pump();

        var start = () => pump.Start();

        start.Should().NotThrow();

        await Task.Delay(100);

        updater.Configured.Should().Be(1, "it was tried once and not retried behind the user's back");
        updater.Checks.Should().Be(0, "a pump that never configured must not go on to poll");
        state.Current.Stage.Should().Be(UpdateStage.Idle);
        notifier.Shown.Should().BeEmpty();
    }

    // The Settings page reaches the same pump, and its buttons must not resurrect a start that
    // failed — `Check now` on an unconfigured updater is the crash again, on a click this time.
    [Fact]
    public async Task A_failed_start_leaves_the_settings_buttons_inert()
    {
        updater.ConfigureThrows = new NullReferenceException("the bridge is not up yet");

        await using var pump = Pump();

        pump.Start();

        pump.CheckNow();
        pump.DownloadNow();

        await Task.Delay(100);

        updater.Checks.Should().Be(0);
        updater.Downloads.Should().Be(0);
    }

    // Asking to download must not turn the toggle on behind the user's back: the request is spent on
    // the pass that honoured it, and the pass after that is back to fetching nothing.
    [Fact]
    public async Task Asking_for_one_download_does_not_arm_the_next_pass()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.DownloadSucceeds = false;

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        pump.DownloadNow();

        await Until(() => updater.Downloads == 1);

        pump.CheckNow();

        await Until(() => updater.Checks >= 3);

        updater.Downloads.Should().Be(1, "the request was spent on the pass that honoured it");
    }

    [Fact]
    public async Task The_toggle_on_downloads_and_becomes_ready()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.ProgressSteps = [10, 60];

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Ready);

        state.Current.Version.Should().Be("1.2.0");
        state.Current.Percent.Should().Be(100);
        updater.Downloads.Should().Be(1);

        notifier.Shown.Should().ContainSingle().Which.Body.Should().Contain("1.2.0");
    }

    // The report that outlives its download. `Progress<T>` posts rather than runs, so this ordering
    // is the normal one, not the unlucky one — and painted over `Ready` it does not heal, because a
    // pass that finds a version already downloaded returns without touching the state again. The
    // page would promise a download that finished and never mention the update waiting to install.
    [Fact]
    public async Task A_report_that_lands_after_the_download_does_not_bury_it()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.ProgressSteps = [10, 60];

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Ready);

        updater.Reporter!.Report(60);

        await Task.Delay(100);

        state.Current.Stage.Should().Be(UpdateStage.Ready);
        state.Current.Percent.Should().Be(100);
    }

    // The same arrival on the other branch. Less costly — the next pass retries a failed download —
    // but "Downloading 60%" while nothing is downloading is a lie for up to six hours.
    [Fact]
    public async Task A_report_that_lands_after_a_failed_download_does_not_bury_it()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.DownloadSucceeds = false;
        updater.ProgressSteps = [10, 60];

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        updater.Reporter!.Report(60);

        await Task.Delay(100);

        state.Current.Stage.Should().Be(UpdateStage.Available);
    }

    // A notification about the app itself has no card behind it, which is why `TaskId` is nullable.
    [Fact]
    public async Task The_notification_points_at_no_task()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump();

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        notifier.Shown.Single().TaskId.Should().BeNull();
    }

    [Fact]
    public async Task A_download_that_fails_goes_back_to_available_so_the_next_pass_retries()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.DownloadSucceeds = false;

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Available && updater.Downloads == 1);

        state.Current.Version.Should().Be("1.2.0");
    }

    // The state ACT ships in until the repository is public. It must read as "ask again later" and
    // never as "you are up to date", which is the lie that would keep someone on an old build.
    [Fact]
    public async Task A_check_that_could_not_complete_is_not_reported_as_up_to_date()
    {
        updater.Answer = UpdateCheck.Failed;

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Unavailable);

        state.Current.Version.Should().BeNull();
        notifier.Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task A_completed_check_records_when_it_happened()
    {
        updater.Answer = UpdateCheck.UpToDate;

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.UpToDate);

        state.Current.CheckedAt.Should().Be(Now);
    }

    // Once ready there is nothing left to look for: the answer cannot improve until the app restarts
    // into the version it is already holding.
    [Fact]
    public async Task A_ready_update_stops_the_pump_asking_again()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump();

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Ready);

        var asked = updater.Checks;

        pump.CheckNow();

        await Task.Delay(100);

        updater.Checks.Should().Be(asked);
    }

    [Fact]
    public async Task Turning_the_toggle_on_checks_rather_than_waiting_for_the_next_interval()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.UpToDate;

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        settings.SetAutoUpdate(true);

        await Until(() => updater.Checks == 1);
    }

    // `Changed` fires for every setting there is. A check per keystroke in a text box is a poor way
    // to treat somebody's network, and the count is the only thing that can prove it does not.
    [Fact]
    public async Task Another_setting_moving_does_not_start_a_check()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);

        await using var pump = Pump(settings);

        pump.Start();

        await Task.Delay(50);

        settings.SetKeepAwake(false);
        settings.SetBlinkYourTurn(false);
        settings.SetMaxConcurrent(3);

        await Task.Delay(100);

        updater.Checks.Should().Be(0);
    }

    // Six hours between passes, and a toast that repeats four times a day is a toast people learn to
    // dismiss without reading.
    [Fact]
    public async Task The_same_version_is_announced_once()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump();

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        pump.CheckNow();
        pump.CheckNow();

        await Task.Delay(100);

        notifier.Shown.Should().ContainSingle();
    }

    // The one toast there is, and it is about a version already on disk — the *Restart and install*
    // button is the thing it is telling you about. Announcing availability as well went with the
    // retired middle position: with the toggle on, the download is in the same pass.
    [Fact]
    public async Task Only_a_downloaded_version_is_announced()
    {
        var settings = Settings();

        settings.SetAutoUpdate(false);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.Available);
        await Task.Delay(100);

        notifier.Shown.Should().BeEmpty("nothing is on disk yet");

        pump.DownloadNow();

        await Until(() => state.Current.Stage is UpdateStage.Ready);
        await Until(() => notifier.Shown.Count == 1);

        notifier.Shown.Single().Body.Should().Contain("1.2.0");
    }

    // Nothing about a version that is no longer on offer should survive it going away.
    [Fact]
    public async Task Falling_back_to_up_to_date_lets_a_later_version_be_announced_again()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump();

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        // A pass that finds a downloaded version returns without asking again, so the state has to be
        // walked back for the second announcement to be reachable at all.
        updater.Answer = UpdateCheck.UpToDate;
        state.Publish(UpdateStatus.Idle);
        pump.CheckNow();

        await Until(() => state.Current.Stage is UpdateStage.UpToDate);

        updater.Answer = UpdateCheck.Found("1.2.0");
        pump.CheckNow();

        await Until(() => notifier.Shown.Count == 2);
    }

    [Fact]
    public async Task A_notifier_that_throws_does_not_stop_the_update()
    {
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(notifier: new ThrowingNotifier());

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Ready);
    }

    [Fact]
    public async Task A_pump_that_never_started_ignores_the_buttons()
    {
        await using var pump = Pump();

        pump.CheckNow();
        pump.DownloadNow();

        await Task.Delay(100);

        updater.Checks.Should().Be(0);
        updater.Downloads.Should().Be(0);
    }

    private UpdatePump Pump(UserSettingsService? settings = null, INotifier? notifier = null)
        => new(
            updater,
            state,
            settings ?? Settings(),
            notifier ?? this.notifier,
            clock,
            NullLogger<UpdatePump>.Instance);

    private UserSettingsService Settings() => new(store, new AppCulture());

    private static async Task Until(Func<bool> settled)
    {
        for (var waited = 0; waited < 500; waited++)
        {
            if (settled())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The pump never reached the expected state.");
    }

    private sealed class ThrowingNotifier : INotifier
    {
        public Task ShowAsync(DesktopNotification notification)
            => throw new InvalidOperationException("no notifications here");
    }
}
