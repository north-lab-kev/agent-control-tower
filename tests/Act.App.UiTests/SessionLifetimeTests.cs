using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// A session ends for two different reasons and only one of them is ACT's doing. Sign-off, restart
// and delete are commands, and the registry has always handled those; the agent's own process
// exiting is not, and nothing used to notice it — so the card kept a dead session that still read
// as live, which is the state every action past the launch boundary asks about first.
public class SessionLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    // The whole point: `IsLive` has to mean what it says once the CLI is gone.
    [Fact]
    public async Task A_session_whose_process_exits_leaves_the_registry()
    {
        var card = Executing();
        var (pump, registry, launcher, _) = PumpOf(card, AgentScript.Start().EndsTurn().Exits(1));

        pump.Start();

        await launcher.RestoreAsync(card, TerminalSize.Default);
        await Settles(() => !registry.IsLive(card.Id));

        registry.IsLive(card.Id).Should().BeFalse();
        registry.LiveCount.Should().Be(0);
    }

    // The reason it matters. `CardRetry` offers the action only while the process is gone, so a
    // registry that never let go answered "still running" for the one card retry exists for.
    [Fact]
    public async Task Retry_is_offered_once_the_process_is_gone()
    {
        var card = Executing();
        var (pump, registry, launcher, _) = PumpOf(card, AgentScript.Start().Exits(1));

        pump.Start();

        await launcher.RestoreAsync(card, TerminalSize.Default);
        await Settles(() => !registry.IsLive(card.Id) && launcher.CanRetry(card));

        card.Column.Should().Be(BoardColumn.YourTurn);
        card.Badge.Should().Be(Badge.Error);
        launcher.CanRetry(card).Should().BeTrue();
    }

    // A clean exit moves nothing — the turn events already said where the work stands — but the
    // process is just as gone, so the card must still be relaunchable.
    [Fact]
    public async Task A_clean_exit_releases_the_session_without_moving_the_card()
    {
        var card = Executing();
        var (pump, registry, launcher, _) = PumpOf(card, AgentScript.Start().Exits(0));

        pump.Start();

        await launcher.RestoreAsync(card, TerminalSize.Default);
        await Settles(() => !registry.IsLive(card.Id));

        card.Column.Should().Be(BoardColumn.Executing);
        registry.IsLive(card.Id).Should().BeFalse();
    }

    // The token and the generated settings file are what a hook posts against, so a session that
    // has ended must take them with it however it ended.
    [Fact]
    public async Task The_hook_token_is_released_with_the_process()
    {
        var card = Executing();
        var (pump, registry, launcher, hooks) = PumpOf(card, AgentScript.Start().Exits(1));

        pump.Start();

        var token = hooks.Register(card.Id);

        await launcher.RestoreAsync(card, TerminalSize.Default);

        // Waits on the token, not on `IsLive`, and that distinction is the whole test: `EndAsync` takes
        // the session out of the dictionary *first* and only releases the token after the dispose that
        // follows, so `!IsLive` is true for a moment while the token is still registered. Waiting on the
        // wrong one of the two made this the suite's only flaky test — it passed alone and failed under
        // the load of four assemblies, which reads exactly like slowness and is not.
        await Settles(() => !hooks.TryResolve(token, out _));

        hooks.TryResolve(token, out _).Should().BeFalse();
    }

    // A restart ends one session and starts another for the same card in the same breath, and the
    // outgoing one's stream drains afterwards. Matched on the instance, that late teardown cannot
    // take the replacement with it.
    [Fact]
    public async Task Ending_a_replaced_session_leaves_its_successor_alone()
    {
        var registry = new SessionRegistry(
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: new FrozenClock(Now))
        {
            Script = AgentScript.Empty(),
        };

        var card = Executing();

        var outgoing = await adapter.ResumeAsync(Resume(card));

        registry.Add(outgoing).Should().BeTrue();

        await registry.EndAsync(card.Id);

        var incoming = await adapter.ResumeAsync(Resume(card));

        registry.Add(incoming).Should().BeTrue();

        (await registry.EndAsync(outgoing)).Should().BeFalse();

        registry.For(card.Id).Should().BeSameAs(incoming);
    }

    // The registry's own half of the double-launch guard: a second session for a live card is
    // refused rather than silently displacing the first, which would orphan a process nothing could
    // ever end.
    [Fact]
    public async Task A_second_session_for_a_live_card_is_refused()
    {
        var registry = new SessionRegistry(
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: new FrozenClock(Now))
        {
            Script = AgentScript.Empty(),
        };

        var card = Executing();

        var first = await adapter.ResumeAsync(Resume(card));
        var second = await adapter.ResumeAsync(Resume(card));

        registry.Add(first).Should().BeTrue();
        registry.Add(second).Should().BeFalse();

        registry.For(card.Id).Should().BeSameAs(first);
        registry.LiveCount.Should().Be(1);

        await second.DisposeAsync();
    }

    // The launcher's half: the queue runner's pass and a click on the same strip both pass a bare
    // liveness check before either has registered anything, and each would then spawn its own CLI.
    // Serialized per card, the loser waits, sees the winner's session, and spawns nothing.
    [Fact]
    public async Task Concurrent_launches_of_one_card_spawn_exactly_one_session()
    {
        var clock = new FrozenClock(Now);
        var hooks = new StubHookEndpoint();
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock)
        {
            Script = AgentScript.Empty(),
        };
        var registry = new SessionRegistry(
            hooks,
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);

        var card = Ready();
        var board = new BoardState(new FakeCardStore([card]), new FakeAttachmentStore(), clock);

        await board.LoadAsync();

        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var launcher = new SessionLauncher(
            [adapter],
            registry,
            board,
            TestNotifications.Dispatcher(settings, new RecordingNotifier()),
            settings,
            new AnyDirectory(),
            new StubAgentConfigFiles(),
            new FakeAttachmentStore(),
            clock,
            new RecordingTelemetrySink(),
            NullLogger<SessionLauncher>.Instance);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                Task.Run(() => launcher.LaunchAsync(card, TerminalSize.Default))));

        results.Should().OnlyContain(result => result.Launched);
        adapter.Launches.Should().ContainSingle();
        registry.LiveCount.Should().Be(1);
    }

    private static AgentResumeRequest Resume(Card card) => new(
        card.Id,
        card.SessionId!,
        card.WorkingDir,
        card.InitialPrompt,
        null,
        card.LaunchConfig,
        TerminalSize.Default);

    private static (SessionEventPump, SessionRegistry, SessionLauncher, StubHookEndpoint) PumpOf(
        Card card,
        AgentScript script)
    {
        var clock = new FrozenClock(Now);
        var hooks = new StubHookEndpoint();
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock) { Script = script };
        var registry = new SessionRegistry(
            hooks,
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var board = new BoardState(new FakeCardStore([card]), new FakeAttachmentStore(), clock);
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var notifications = TestNotifications.Dispatcher(settings, new RecordingNotifier());

        board.LoadAsync().GetAwaiter().GetResult();

        var launcher = new SessionLauncher(
            [adapter],
            registry,
            board,
            notifications,
            settings,
            new AnyDirectory(),
            new StubAgentConfigFiles(),
            new FakeAttachmentStore(),
            clock,
            new RecordingTelemetrySink(),
            NullLogger<SessionLauncher>.Instance);

        var pump = new SessionEventPump(
            registry,
            board,
            notifications,
            clock,
            NullLogger<SessionEventPump>.Instance);

        return (pump, registry, launcher, hooks);
    }

    // The scripted session runs on its own task, so the assertions wait on the condition rather
    // than on a duration — a sleep long enough to be safe on CI is a slow suite everywhere else.
    //
    // The budget is a *ceiling*, not a cost: the loop leaves the moment the condition holds, so a
    // passing test is as fast at 6s as at 2s and only a genuine hang pays the difference. Raised from
    // 2s for headroom under a loaded machine — but note that the flake it was raised for
    // was **not** slowness, and raising this did not fix it: the caller was waiting on a condition that
    // goes true a beat before the one it asserts. Wait on the property under test, not on a neighbour
    // of it, and this loop's ceiling stops mattering.
    private static async Task Settles(Func<bool> until)
    {
        for (var attempt = 0; attempt < 600 && !until(); attempt++)
            await Task.Delay(10);
    }

    private static Card Executing() => new()
    {
        Number = 1042,
        Title = "Mid-flight",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        WorkingDir = "/dev/act",
        SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11",
    };

    private static Card Ready() => new()
    {
        Number = 1043,
        Title = "Waiting to go",
        Column = BoardColumn.Ready,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
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
