using Act.App.Cards;
using Act.App.Notifications;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// Launching claims the card; resuming does not. That one distinction is what separates the
// launcher's four entry points, and these are the promises on either side of it — what a launch
// stamps, what a retry says, and the two different things a failed spawn does depending on which
// kind of start it was.
public class SessionLaunchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Launching_claims_the_card()
    {
        var card = Ready();
        var (launcher, _, _, _) = LauncherOf(card);

        (await launcher.LaunchAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        card.Column.Should().Be(BoardColumn.Executing);
        card.Badge.Should().Be(Badge.Running);
        card.LaunchedAt.Should().Be(Now);
        card.SessionId.Should().NotBeNullOrEmpty();
        card.Transitions.Last().Reason.Should().Be(TransitionReason.Launched);
    }

    // The armed instant belongs to the wait: left behind, a card dragged back to Ready would point
    // at a window boundary that passed while it was running.
    [Fact]
    public async Task Launching_forgets_the_armed_instant()
    {
        var card = Ready();
        card.Schedule = TaskSchedule.NextWindow;
        card.EligibleAt = Now.AddHours(1);

        var (launcher, _, _, _) = LauncherOf(card);

        await launcher.LaunchAsync(card, TerminalSize.Default);

        card.EligibleAt.Should().BeNull();
    }

    // A card that has never bound a session is launched; one that has is resumed. The card decides,
    // not the entry point.
    [Fact]
    public async Task A_card_with_no_session_goes_through_the_launch_door()
    {
        var card = Ready();
        var (launcher, _, adapter, _) = LauncherOf(card);

        await launcher.LaunchAsync(card, TerminalSize.Default);

        adapter.Launches.Should().ContainSingle();
        adapter.Resumes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retrying_resumes_with_the_interrupted_message_and_says_so()
    {
        var card = Failed();
        var (launcher, _, adapter, _) = LauncherOf(card);

        launcher.CanRetry(card).Should().BeTrue();

        (await launcher.RetryAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        adapter.Resumes.Should().ContainSingle()
            .Which.Message.Should().Be(RetryInstruction.Message);

        card.Transitions.Last().Reason.Should().Be(TransitionReason.Retried);
        card.Column.Should().Be(BoardColumn.Executing);
    }

    // A launch that never started is the work failing, so the card shows it rather than sitting in
    // Ready looking untouched — and it is a card wanting the user, so it is announced.
    [Fact]
    public async Task A_launch_that_cannot_spawn_puts_the_card_in_error()
    {
        var card = Ready();
        var (launcher, _, adapter, notifier) = LauncherOf(card);

        adapter.Fails = new InvalidOperationException("claude: not found");

        var result = await launcher.LaunchAsync(card, TerminalSize.Default);

        result.Launched.Should().BeFalse();
        result.Message.Should().Be("claude: not found");

        card.Column.Should().Be(BoardColumn.YourTurn);
        card.Badge.Should().Be(Badge.Error);
        card.Transitions.Last().Reason.Should().Be(TransitionReason.LaunchFailed);
        card.Transitions.Last().Note.Should().Be("claude: not found");

        notifier.Shown.Should().ContainSingle();
    }

    // The mirror, and the reason the two are not one branch: a terminal that could not be brought
    // back is not a new failure of the work, so the card stays exactly where the rules left it and
    // nobody is interrupted about it.
    [Fact]
    public async Task A_restore_that_cannot_spawn_leaves_the_card_alone()
    {
        var card = Executing();
        var (launcher, _, adapter, notifier) = LauncherOf(card);

        adapter.Fails = new InvalidOperationException("no such session");

        (await launcher.RestoreAsync(card, TerminalSize.Default)).Launched.Should().BeFalse();

        card.Column.Should().Be(BoardColumn.Executing);
        card.Badge.Should().Be(Badge.Running);
        card.Transitions.Last().Reason.Should().Be(TransitionReason.RestoreFailed);

        notifier.Shown.Should().BeEmpty();
    }

    // Never silently: a launch that did not get what the card asked for records the substitution
    // under its own reason, so "why is this running at a different effort" stays answerable.
    [Fact]
    public async Task A_substituted_effort_is_named_on_the_timeline()
    {
        var card = Ready();
        card.LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel, Effort = "max" };

        var (launcher, _, _, _) = LauncherOf(card);

        await launcher.LaunchAsync(card, TerminalSize.Default);

        var stamped = card.Transitions.Last();

        stamped.Reason.Should().Be(TransitionReason.LaunchedWithAdjustments);
        stamped.Note.Should().Contain("max");
    }

    // The guard reads the card being *started*, and refuses without touching it: the folder frees up
    // on its own, so there is nothing to undo when it does.
    [Fact]
    public async Task A_folder_someone_else_holds_makes_the_launch_wait()
    {
        var holder = Executing();
        holder.Number = 7;

        var card = Ready();
        var (launcher, _, adapter, _) = LauncherOf(holder, card);

        var result = await launcher.LaunchAsync(card, TerminalSize.Default);

        result.Launched.Should().BeFalse();
        result.Waiting.Should().BeTrue();

        card.Column.Should().Be(BoardColumn.Ready);
        card.Transitions.Should().BeEmpty();
        adapter.Launches.Should().BeEmpty();
    }

    // An archived card is openable and inert. Ready is the column a launch is offered from, so it is
    // the one that proves the archive outranks it.
    [Fact]
    public async Task An_archived_card_is_not_launched()
    {
        var card = Ready();
        card.DeletedAt = Now;

        var (launcher, _, adapter, _) = LauncherOf(card);

        launcher.CanLaunch(card).Should().BeFalse();

        (await launcher.LaunchAsync(card, TerminalSize.Default)).Launched.Should().BeFalse();

        adapter.Launches.Should().BeEmpty();
        card.Column.Should().Be(BoardColumn.Ready);
    }

    // The other half, and the one the terminal page walks into: opening an archived card must not
    // resume the session it still carries.
    [Fact]
    public async Task An_archived_card_is_not_resumed()
    {
        var card = Executing();
        card.ArchivedAt = Now;

        var (launcher, _, adapter, _) = LauncherOf(card);

        launcher.CanRestart(card).Should().BeFalse();

        (await launcher.RestoreAsync(card, TerminalSize.Default)).Launched.Should().BeFalse();

        adapter.Resumes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_card_that_is_already_running_is_not_launched_twice()
    {
        var card = Ready();
        var (launcher, registry, adapter, _) = LauncherOf(card);

        await launcher.LaunchAsync(card, TerminalSize.Default);
        (await launcher.LaunchAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        registry.LiveCount.Should().Be(1);
        adapter.Launches.Should().ContainSingle();
    }

    private static (SessionLauncher, SessionRegistry, MockAgentAdapter, RecordingNotifier) LauncherOf(
        params Card[] cards)
    {
        var clock = new FrozenClock(Now);
        var adapter = new MockAgentAdapter(AgentType.ClaudeCode, clock: clock);
        var registry = new SessionRegistry(
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<SessionRegistry>.Instance);
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), clock);
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var notifier = new RecordingNotifier();

        board.LoadAsync().GetAwaiter().GetResult();

        var launcher = new SessionLauncher(
            [adapter],
            registry,
            board,
            TestNotifications.Dispatcher(settings, notifier),
            settings,
            new PassThroughDirectories(),
            new FakeAttachmentStore(),
            clock,
            NullLogger<SessionLauncher>.Instance);

        return (launcher, registry, adapter, notifier);
    }

    private static Card Ready() => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        Column = BoardColumn.Ready,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
    };

    private static Card Executing() => new()
    {
        Number = 1043,
        Title = "Mid-flight",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        WorkingDir = "/dev/act",
        SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11",
    };

    private static Card Failed() => new()
    {
        Number = 1044,
        Title = "Died mid-turn",
        Column = BoardColumn.YourTurn,
        Badge = Badge.Error,
        WorkingDir = "/dev/other",
        SessionId = "1b2c3d4e-0000-4a2c-9f4d-2f0a3f7c1e22",
    };

    private sealed class PassThroughDirectories : IWorkingDirectories
    {
        public string Home => "/home/act";

        public string Resolve(string workingDir) => workingDir.Trim().Replace('\\', '/');

        public bool Exists(string workingDir) => true;

        public void Create(string workingDir) { }

        public PathCheck Check(string workingDir) => PathCheck.Found(Resolve(workingDir));

        public string NearestDirectory(string? path) => path is null ? Home : Resolve(path);

        public DirectoryListing List(string? path, bool includeFiles = false) => new(path, null, []);
    }
}
