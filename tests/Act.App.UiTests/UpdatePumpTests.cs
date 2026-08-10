using Act.App.Desktop;
using Act.App.Settings;
using Act.App.Updates;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The policy half of auto-update: when ACT asks, how far it goes once told, and what it says about
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

    // The whole point of the switch. Not "checks and discards the answer" — nothing may reach the
    // network at all, and a count is the only way to say so.
    [Fact]
    public async Task Off_never_asks_anything()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.Off);

        await using var pump = Pump(settings);

        pump.Start();

        await Task.Delay(100);

        updater.Checks.Should().Be(0);
        state.Current.Stage.Should().Be(UpdateStage.Idle);
        notifier.Shown.Should().BeEmpty();
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

    [Fact]
    public async Task Notify_only_finds_the_version_and_stops_there()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        state.Current.Version.Should().Be("1.2.0");
        updater.Downloads.Should().Be(0);
        notifier.Shown.Should().ContainSingle().Which.Body.Should().Contain("1.2.0");
    }

    // Without this, `NotifyOnly` is a setting that names a version and offers no way to have it.
    [Fact]
    public async Task Notify_only_downloads_when_asked()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        pump.DownloadNow();

        await Until(() => state.Current.Stage is UpdateStage.Ready);

        updater.Downloads.Should().Be(1);
    }

    // And having asked once must not turn the policy into the other one.
    [Fact]
    public async Task Asking_once_does_not_arm_the_next_pass()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");
        updater.DownloadSucceeds = false;

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Available);

        pump.DownloadNow();

        await Until(() => updater.Downloads == 1);

        pump.CheckNow();

        await Until(() => updater.Checks >= 3);

        updater.Downloads.Should().Be(1, "the request was spent on the pass that honoured it");
    }

    [Fact]
    public async Task The_default_policy_downloads_and_becomes_ready()
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
    public async Task Turning_the_policy_on_checks_rather_than_waiting_for_the_next_interval()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.Off);
        updater.Answer = UpdateCheck.UpToDate;

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Current.Stage is UpdateStage.Idle);

        settings.SetUpdates(UpdatePolicy.NotifyAndDownload);

        await Until(() => updater.Checks == 1);
    }

    // `Changed` fires for every setting there is. A check per keystroke in a text box is a poor way
    // to treat somebody's network, and the count is the only thing that can prove it does not.
    [Fact]
    public async Task Another_setting_moving_does_not_start_a_check()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.Off);

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
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        pump.CheckNow();
        pump.CheckNow();

        await Until(() => updater.Checks >= 3);

        notifier.Shown.Should().ContainSingle();
    }

    // Downloading it is news even when its existence already was: the two say different things about
    // what happens next.
    [Fact]
    public async Task Becoming_ready_is_announced_even_after_being_announced_as_available()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        settings.SetUpdates(UpdatePolicy.NotifyAndDownload);

        await Until(() => state.Current.Stage is UpdateStage.Ready);
        await Until(() => notifier.Shown.Count == 2);

        notifier.Shown[1].Title.Should().NotBe(notifier.Shown[0].Title);
    }

    // Nothing about a version that is no longer on offer should survive it going away.
    [Fact]
    public async Task Falling_back_to_up_to_date_lets_a_later_version_be_announced_again()
    {
        var settings = Settings();

        settings.SetUpdates(UpdatePolicy.NotifyOnly);
        updater.Answer = UpdateCheck.Found("1.2.0");

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => notifier.Shown.Count == 1);

        updater.Answer = UpdateCheck.UpToDate;
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
