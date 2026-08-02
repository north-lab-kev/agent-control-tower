using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.App.UiTests;

// Restart replaces the terminal, never the work. These are the promises that separate it from the
// kill action it took over from: the session id survives, the card does not move, and the pty that
// was there is gone before a second one exists for the same session.
public class SessionRestartTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Restarting_resumes_the_same_session_in_a_new_terminal()
    {
        var card = Executing();
        var (launcher, registry, adapter) = LauncherOf(card);

        await launcher.RestoreAsync(card, TerminalSize.Default);
        var first = registry.For(card.Id);

        (await launcher.RestartAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        adapter.Resumes.Last().SessionId.Should().Be(card.SessionId);
        registry.For(card.Id).Should().NotBeSameAs(first);
    }

    // The whole reason it exists rather than a kill: a session that is still running is exactly the
    // one whose terminal you want replaced. Two ptys on one session id would be two CLIs writing one
    // transcript, so the old one has to be gone before the new one exists.
    [Fact]
    public async Task A_live_session_is_ended_before_the_new_one_starts()
    {
        var card = Executing();
        var (launcher, registry, _) = LauncherOf(card);

        await launcher.RestoreAsync(card, TerminalSize.Default);
        registry.IsLive(card.Id).Should().BeTrue();

        await launcher.RestartAsync(card, TerminalSize.Default);

        registry.LiveCount.Should().Be(1);
    }

    // Resuming a session is not a claim that anything about the work changed.
    [Fact]
    public async Task The_card_does_not_move()
    {
        var card = Executing();
        card.Column = BoardColumn.YourTurn;
        card.Badge = Badge.NeedsAnswer;

        var (launcher, _, _) = LauncherOf(card);

        await launcher.RestartAsync(card, TerminalSize.Default);

        card.Column.Should().Be(BoardColumn.YourTurn);
        card.Badge.Should().Be(Badge.NeedsAnswer);
    }

    // The scrollback is discarded rather than lost, and the timeline is the only place that can
    // say so afterwards.
    [Fact]
    public async Task Restarting_is_on_the_timeline_under_its_own_reason()
    {
        var card = Executing();
        var (launcher, _, _) = LauncherOf(card);

        await launcher.RestartAsync(card, TerminalSize.Default);

        card.Transitions.Last().Reason.Should().Be(TransitionReason.TerminalRestarted);
    }

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    public async Task A_card_that_was_never_launched_has_no_terminal_to_restart(BoardColumn column)
    {
        var card = Executing();
        card.Column = column;
        card.SessionId = null;

        var (launcher, _, adapter) = LauncherOf(card);

        launcher.CanRestart(card).Should().BeFalse();
        (await launcher.RestartAsync(card, TerminalSize.Default)).Launched.Should().BeFalse();
        adapter.Resumes.Should().BeEmpty();
    }

    // A signed-off card opens on its history, so the terminal showing that history is restartable
    // like any other — and restarting it must not un-complete anything.
    [Fact]
    public async Task A_completed_card_can_restart_and_stays_completed()
    {
        var card = Executing();
        card.Column = BoardColumn.Completed;

        var (launcher, _, _) = LauncherOf(card);

        (await launcher.RestartAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        card.Column.Should().Be(BoardColumn.Completed);
    }

    private static (SessionLauncher, SessionRegistry, MockAgentAdapter) LauncherOf(params Card[] cards)
    {
        var clock = new FrozenClock(Now);
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock);
        var registry = new SessionRegistry(new StubHookEndpoint(), new StubAgentConfigFiles());
        var board = new BoardState(new FakeCardStore(cards), clock);
        var settings = new UserSettingsService(new MemorySettingsStore(), new AppCulture());

        board.LoadAsync().GetAwaiter().GetResult();

        return (new SessionLauncher(
            [adapter],
            registry,
            board,
            TestNotifications.Dispatcher(settings, new RecordingNotifier()),
            settings,
            new PassThroughDirectories(),
            clock), registry, adapter);
    }

    private static Card Executing() => new()
    {
        Title = "Mid-flight",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        WorkingDir = "/dev/act",
        SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11",
    };

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private UserSettings settings = new();

        public UserSettings Load() => settings;

        public void Save(UserSettings updated) => settings = updated;
    }

    private sealed class PassThroughDirectories : IWorkingDirectories
    {
        public string Home => "/home/act";

        public string Resolve(string workingDir) => workingDir;

        public bool Exists(string workingDir) => true;

        public void Create(string workingDir) { }

        public PathCheck Check(string workingDir) => PathCheck.Found(workingDir);

        public DirectoryListing List(string? path, bool includeFiles = false) => new(path, null, []);
    }
}
