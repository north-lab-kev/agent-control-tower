using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Telemetry;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// A launch is a use of the app; a resume is not. `SessionRestorer` replays one per live card on every
// startup and a terminal restart is the same session coming back, so counting those would report a
// card as started several times over and bury the launches that actually happened.
public class SessionLaunchTelemetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_launch_is_reported_once()
    {
        var card = Ready();
        var (launcher, _, telemetry) = LauncherOf(card);

        await launcher.LaunchAsync(card, TerminalSize.Default);

        var launched = telemetry.Named(TelemetryEvents.Names.TaskLaunched).Should().ContainSingle().Subject;

        launched.Properties[TelemetryProperties.Agent].Should().Be(AgentType.ClaudeCode);
        launched.Properties[TelemetryProperties.AttachmentCount].Should().Be(0);
    }

    [Fact]
    public async Task A_restore_reports_nothing()
    {
        var card = Executing();
        var (launcher, _, telemetry) = LauncherOf(card);

        await launcher.RestoreAsync(card, TerminalSize.Default);

        telemetry.Captured.Should().BeEmpty();
    }

    [Fact]
    public async Task A_terminal_restart_reports_nothing()
    {
        var card = Executing();
        var (launcher, _, telemetry) = LauncherOf(card);

        await launcher.RestoreAsync(card, TerminalSize.Default);
        await launcher.RestartAsync(card, TerminalSize.Default);

        telemetry.Captured.Should().BeEmpty();
    }

    // A retry *does* count, and the distinction is the launcher's own: it claims the card and moves it
    // to Executing, so it is a start the user asked for rather than a re-attach ACT performed.
    [Fact]
    public async Task A_retry_is_reported()
    {
        var card = Failed();
        var (launcher, _, telemetry) = LauncherOf(card);

        await launcher.RetryAsync(card, TerminalSize.Default);

        telemetry.Named(TelemetryEvents.Names.TaskLaunched).Should().ContainSingle();
    }

    [Fact]
    public async Task A_launch_that_never_started_reports_nothing()
    {
        var card = Ready();
        var (launcher, _, telemetry) = LauncherOf(card);

        card.Column = BoardColumn.Preparing;

        (await launcher.LaunchAsync(card, TerminalSize.Default)).Launched.Should().BeFalse();

        telemetry.Captured.Should().BeEmpty();
    }

    private static (SessionLauncher, SessionRegistry, RecordingTelemetrySink) LauncherOf(params Card[] cards)
    {
        var clock = new FrozenClock(Now);
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock);
        var registry = new SessionRegistry(
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), clock);
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var telemetry = new RecordingTelemetrySink();

        board.LoadAsync().GetAwaiter().GetResult();

        return (new SessionLauncher(
            [adapter],
            registry,
            board,
            TestNotifications.Dispatcher(settings, new RecordingNotifier()),
            settings,
            new AnyDirectory(),
            new FakeAttachmentStore(),
            clock,
            telemetry,
            NullLogger<SessionLauncher>.Instance), registry, telemetry);
    }

    private static Card Ready() => new()
    {
        Number = 1044,
        Title = "Rename the widget",
        Column = BoardColumn.Ready,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
    };

    private static Card Executing() => new()
    {
        Number = 1045,
        Title = "Mid-flight",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        WorkingDir = "/dev/act",
        SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11",
    };

    private static Card Failed() => new()
    {
        Number = 1046,
        Title = "Fell over",
        Column = BoardColumn.YourTurn,
        Badge = Badge.Error,
        WorkingDir = "/dev/act",
        SessionId = "1b2c3d4e-0000-4a2c-9f4d-2f0a3f7c1e22",
    };

    private sealed class AnyDirectory : IWorkingDirectories
    {
        public string Home => "/home/act";

        public string Resolve(string workingDir) => workingDir;

        public bool Exists(string workingDir) => true;

        public void Create(string workingDir) { }

        public PathCheck Check(string workingDir) => PathCheck.Found(workingDir);

        public string NearestDirectory(string? path) => path ?? Home;

        public DirectoryListing List(string? path, bool includeFiles = false) => new(path, null, []);
    }
}
