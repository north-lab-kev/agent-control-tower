using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The unattended half of Ready → Executing. `LaunchQueue` decides and is tested in Core; what is
// tested here is what the runner does with that decision — the arming it writes down before
// anything starts, the launches it hands the launcher, and the sleep hold it owns.
//
// Every test drives one real pass through `Start`, because the single-flight gate and the event
// subscriptions are half of what the runner is.
public class QueueRunnerTests : ComponentTest
{
    [Fact]
    public async Task A_due_card_is_launched_and_claimed()
    {
        var card = Ready(1);

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Executing);
        card.Badge.Should().Be(Badge.Running);
        Registry.For(card.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task A_manual_card_is_never_taken_by_the_queue()
    {
        var card = Ready(1);
        card.Schedule = TaskSchedule.Manual;

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Ready);
        Queue.Latest.Launch.Should().BeEmpty();
        Queue.Latest.Holds.Should().BeEmpty("a manual card is not in the queue at all");
    }

    [Fact]
    public async Task The_pause_switch_holds_every_ready_card()
    {
        Settings.SetAutoExecutionPaused(true);

        var card = Ready(1);

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Ready);
        Queue.Latest.Holds.Should().ContainSingle()
            .Which.Value.Reason.Should().Be(LaunchHold.Paused);
    }

    [Fact]
    public async Task The_cap_is_what_decides_how_many_of_a_queue_start()
    {
        Settings.SetMaxConcurrent(1);
        Settings.SetPreventConcurrentWorkingDir(false);

        var (head, next) = await AQueueOfTwo();

        await Pass();

        head.Column.Should().Be(BoardColumn.Executing);
        next.Column.Should().Be(BoardColumn.Ready);
        Queue.Latest.Holds.Should().ContainKey(next.Id)
            .WhoseValue.Reason.Should().Be(LaunchHold.Slot);
    }

    // Written before anything launches, so a relative schedule that has just been resolved survives
    // a crash on the very next line — otherwise the card is armed against a different window every
    // time ACT starts.
    [Fact]
    public async Task A_next_window_card_has_its_instant_written_down_before_anything_starts()
    {
        var card = Ready(1);
        card.Schedule = TaskSchedule.NextWindow;

        var resets = Now.AddHours(2);

        Usage.Publish(UsageProbeResult.Of(new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 10, resets)],
            Now,
            LimitReached: false,
            Plan: "pro")));

        await BoardWith(card);
        await Pass();

        card.EligibleAt.Should().Be(resets);
        card.Column.Should().Be(BoardColumn.Ready, "the window has not opened yet");

        Cards.Updated.Should().Contain(card, "the arming has to survive a restart");
    }

    [Fact]
    public async Task An_armed_card_launches_once_its_instant_has_passed()
    {
        var card = Ready(1);
        card.Schedule = TaskSchedule.NextWindow;
        card.EligibleAt = Now.AddMinutes(-1);

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Executing);
    }

    [Fact]
    public async Task A_card_whose_agent_has_no_budget_left_waits_for_the_window()
    {
        var card = Ready(1);

        var resets = Now.AddHours(1);

        Usage.Publish(UsageProbeResult.Of(new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 100, resets)],
            Now,
            LimitReached: true,
            Plan: "pro")));

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Ready);

        var hold = Queue.Latest.Holds.Should().ContainKey(card.Id).WhoseValue;
        hold.Reason.Should().Be(LaunchHold.UsageLimit);
        hold.Until.Should().Be(resets);
    }

    // A refusal is already recorded on the card by the launcher, so the runner neither retries nor
    // moves anything of its own — and the card it left in error is no longer in the queue, which is
    // what stops a failing spawn being attempted every twenty seconds for the life of the process.
    [Fact]
    public async Task A_launch_that_cannot_spawn_is_left_where_the_launcher_put_it()
    {
        Claude.Fails = new InvalidOperationException("claude: not found");

        var card = Ready(1);

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.YourTurn);
        card.Badge.Should().Be(Badge.Error);
        card.Transitions.Last().Reason.Should().Be(TransitionReason.LaunchFailed);

        Queue.Evaluate().Launch.Should().BeEmpty("the card is no longer Ready");
    }

    [Fact]
    public async Task A_second_ready_card_in_the_same_folder_waits_behind_the_first()
    {
        Settings.SetPreventConcurrentWorkingDir(true);
        Settings.SetMaxConcurrent(5);

        var (head, next) = await AQueueOfTwo();

        await Pass();

        head.Column.Should().Be(BoardColumn.Executing);
        next.Column.Should().Be(BoardColumn.Ready);

        var hold = Queue.Latest.Holds.Should().ContainKey(next.Id).WhoseValue;
        hold.Reason.Should().Be(LaunchHold.WorkingDir);
        hold.Blocker?.Id.Should().Be(head.Id);
    }

    [Fact]
    public async Task An_agent_switched_off_holds_its_cards_rather_than_launching_them()
    {
        var defaults = Settings.Defaults(AgentType.ClaudeCode);
        defaults.Enabled = false;
        Settings.SetDefaults(defaults);

        var card = Ready(1);

        await BoardWith(card);
        await Pass();

        card.Column.Should().Be(BoardColumn.Ready);
        Queue.Latest.Holds.Should().ContainKey(card.Id)
            .WhoseValue.Reason.Should().Be(LaunchHold.AgentDisabled);
    }

    // The board renders its hold chips from the runner rather than evaluating for itself, so a pass
    // that changes nothing still has to announce — a toggled pause is not a card write and
    // `BoardState.Changed` never fires for it.
    [Fact]
    public async Task Every_pass_announces_so_the_board_can_redraw()
    {
        Settings.SetAutoExecutionPaused(true);

        await BoardWith(Ready(1));

        var announced = 0;
        Queue.Evaluated += () => announced++;

        await Pass();

        announced.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Latest_is_what_the_last_pass_decided()
    {
        Settings.SetAutoExecutionPaused(true);

        await BoardWith(Ready(1));
        await Pass();

        Queue.Latest.Holds.Should().ContainSingle();

        await WhenEvaluated(() => Settings.SetAutoExecutionPaused(false));

        Queue.Latest.Holds.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_pending_leaves_the_machine_free_to_sleep()
    {
        Settings.SetKeepAwake(true);

        await BoardWith(Card(1, BoardColumn.Preparing));
        await Pass();

        Sleep.IsHeld.Should().BeFalse();
    }

    // The whole point of the overnight queue is a machine still awake when the window opens, which
    // is why a Ready card counts only when it could launch on its own.
    [Fact]
    public async Task A_scheduled_card_that_has_not_started_yet_keeps_the_machine_awake()
    {
        Settings.SetKeepAwake(true);
        Settings.SetAutoExecutionPaused(true);

        var card = Ready(1);
        card.Schedule = TaskSchedule.SpecificDateTime;
        card.ScheduledFor = Now.AddHours(6);

        await BoardWith(card);
        await Pass();

        Sleep.IsHeld.Should().BeFalse("nothing can start while the switch is off");

        await WhenEvaluated(() => Settings.SetAutoExecutionPaused(false));

        Sleep.IsHeld.Should().BeTrue();
    }

    [Fact]
    public async Task A_running_card_keeps_the_machine_awake_even_while_paused()
    {
        Settings.SetKeepAwake(true);
        Settings.SetAutoExecutionPaused(true);

        var running = Card(1, BoardColumn.Executing);
        running.Badge = Badge.Running;

        await BoardWith(running);
        await Pass();

        Sleep.IsHeld.Should().BeTrue();
    }

    [Fact]
    public async Task The_hold_is_not_taken_when_the_user_did_not_ask_for_it()
    {
        Settings.SetKeepAwake(false);

        var running = Card(1, BoardColumn.Executing);
        running.Badge = Badge.Running;

        await BoardWith(running);
        await Pass();

        Sleep.IsHeld.Should().BeFalse();
    }

    // Released on the way out, or a machine stays awake because ACT was closed while something was
    // running.
    [Fact]
    public async Task Shutting_down_releases_the_hold()
    {
        Settings.SetKeepAwake(true);

        var running = Card(1, BoardColumn.Executing);
        running.Badge = Badge.Running;

        await BoardWith(running);
        await Pass();

        Sleep.IsHeld.Should().BeTrue();

        await Queue.DisposeAsync();

        Sleep.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        await BoardWith(Ready(1));
        await Pass();

        await Queue.DisposeAsync();

        var again = async () => await Queue.DisposeAsync();

        await again.Should().NotThrowAsync();
    }

    // One pass, driven the way production drives it. `Evaluated` is raised at the end of a pass, so
    // waiting for it is waiting for the arming, the launches and the sleep decision to be done.
    private Task Pass() => WhenEvaluated(Queue.Start);

    // The subscription has to be in place before the trigger runs: a pass runs synchronously up to
    // its first await, so a handler attached afterwards can miss the announcement it is waiting for.
    private async Task WhenEvaluated(Action trigger)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEvaluated() => done.TrySetResult();

        Queue.Evaluated += OnEvaluated;

        try
        {
            trigger();

            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Queue.Evaluated -= OnEvaluated;
        }
    }

    // Positions stamped rather than left at zero, because the queue is the Ready column read top to
    // bottom and `CardOrder` tiebreaks equal positions on `Number` *descending* — two cards at order
    // zero would run in the reverse of the order a test reads.
    private async Task<(Card Head, Card Next)> AQueueOfTwo()
    {
        var head = Ready(1);
        head.Order = 1;

        var next = Ready(2);
        next.Order = 2;

        await BoardWith(head, next);

        return (head, next);
    }

    private static Card Ready(int number)
    {
        var card = Card(number, BoardColumn.Ready);

        card.Schedule = TaskSchedule.Now;

        return card;
    }

    private static Card Card(int number, BoardColumn column) => new()
    {
        Number = number,
        Title = $"Card {number}",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
    };
}
