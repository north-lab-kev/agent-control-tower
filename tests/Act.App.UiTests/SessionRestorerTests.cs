using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// Startup's re-attach pass: every mid-flight card gets its terminal back, all at once — the restores
// are independent spawns — and best effort, so one agent whose spawn fails must not stop the others.
public class SessionRestorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_restorable_card_gets_its_terminal_back()
    {
        var cards = Enumerable.Range(0, 3).Select(index => Executing(1040 + index)).ToArray();
        var (restorer, registry, _) = RestorerOf(cards);

        await restorer.RestoreAllAsync();

        registry.LiveCount.Should().Be(3);
    }

    [Fact]
    public async Task One_failed_restore_does_not_stop_the_others()
    {
        var broken = Executing(1040);
        var fine = Executing(1041, AgentType.Codex);
        var (restorer, registry, claude) = RestorerOf(broken, fine);

        claude.Fails = new IOException("the binary moved");

        await restorer.RestoreAllAsync();

        registry.IsLive(fine.Id).Should().BeTrue();
        registry.IsLive(broken.Id).Should().BeFalse();
    }

    [Fact]
    public async Task A_card_that_is_not_mid_flight_is_left_alone()
    {
        var completed = Executing(1040);
        completed.Column = BoardColumn.Completed;

        var (restorer, registry, _) = RestorerOf(completed);

        await restorer.RestoreAllAsync();

        registry.LiveCount.Should().Be(0);
    }

    private static (SessionRestorer, SessionRegistry, MockAgentAdapter) RestorerOf(params Card[] cards)
    {
        var clock = new FrozenClock(Now);
        var claude = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock) { Script = AgentScript.Empty() };
        var codex = new MockAgentAdapter(AgentType.Codex, clock: clock) { Script = AgentScript.Empty() };
        var registry = new SessionRegistry(
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), clock);

        board.LoadAsync().GetAwaiter().GetResult();

        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var launcher = new SessionLauncher(
            [claude, codex],
            registry,
            board,
            TestNotifications.Dispatcher(settings, new RecordingNotifier()),
            settings,
            new StubWorkingDirectories(),
            new FakeAttachmentStore(),
            clock,
            new RecordingTelemetrySink(),
            NullLogger<SessionLauncher>.Instance);

        return (new SessionRestorer(board, launcher, NullLogger<SessionRestorer>.Instance), registry, claude);
    }

    private static Card Executing(int number, AgentType agent = AgentType.ClaudeCode) => new()
    {
        Number = number,
        Title = $"Mid-flight {number}",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        AgentType = agent,
        WorkingDir = "/dev/act",
        SessionId = Guid.NewGuid().ToString(),
    };
}
