using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class LaunchQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_due_card_with_nothing_in_its_way_launches()
    {
        var card = Ready(1040, TaskSchedule.Now);

        var evaluation = Evaluate([card]);

        evaluation.Launch.Should().ContainSingle().Which.Should().Be(card);
        evaluation.Holds.Should().BeEmpty();
    }

    // Manual is the per-task opt-out and it stays one: it is not held, it is simply not queued, and
    // a chip saying otherwise would be claiming ACT intends to start it.
    [Fact]
    public void A_manual_card_is_neither_launched_nor_held()
    {
        var evaluation = Evaluate([Ready(1040, TaskSchedule.Manual)]);

        evaluation.Launch.Should().BeEmpty();
        evaluation.Holds.Should().BeEmpty();
    }

    [Fact]
    public void A_card_not_yet_due_is_neither_launched_nor_held()
    {
        var card = Ready(1040, TaskSchedule.SpecificDateTime);
        card.ScheduledFor = Now.AddHours(3);

        var evaluation = Evaluate([card]);

        evaluation.Launch.Should().BeEmpty();
        evaluation.Holds.Should().BeEmpty();
    }

    [Fact]
    public void Pausing_holds_everything_that_was_due()
    {
        var card = Ready(1040, TaskSchedule.Now);

        Hold(Evaluate([card], Policy(paused: true)), card).Reason.Should().Be(LaunchHold.Paused);
    }

    [Fact]
    public void An_agent_switched_off_holds_its_cards()
    {
        var card = Ready(1040, TaskSchedule.Now);
        var policy = Policy() with { EnabledAgents = new HashSet<AgentType> { AgentType.Codex } };

        Hold(Evaluate([card], policy), card).Reason.Should().Be(LaunchHold.AgentDisabled);
    }

    [Fact]
    public void An_exhausted_quota_holds_with_the_instant_it_resets()
    {
        var spent = new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 100, Now.AddHours(2))],
            Now,
            false,
            null);

        var card = Ready(1040, TaskSchedule.Now);

        var hold = Hold(Evaluate([card], usage: _ => spent), card);

        hold.Reason.Should().Be(LaunchHold.UsageLimit);
        hold.Until.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void A_busy_folder_holds_and_names_the_card_holding_it()
    {
        var holder = Ready(1039, TaskSchedule.Now);
        holder.Column = BoardColumn.Executing;
        holder.Badge = Badge.Running;

        var card = Ready(1040, TaskSchedule.Now);

        var hold = Hold(Evaluate([holder, card]), card);

        hold.Reason.Should().Be(LaunchHold.WorkingDir);
        hold.Blocker.Should().Be(holder);
    }

    // The bug this exists to stop: two Ready cards in one folder, both eligible, both launched by
    // the same pass because neither was Executing when the other was judged.
    [Fact]
    public void Two_ready_cards_in_one_folder_do_not_both_launch()
    {
        var first = Ready(1040, TaskSchedule.Now);
        var second = Ready(1041, TaskSchedule.Now);

        var evaluation = Evaluate([first, second]);

        evaluation.Launch.Should().ContainSingle().Which.Should().Be(first);
        Hold(evaluation, second).Reason.Should().Be(LaunchHold.WorkingDir);
    }

    [Fact]
    public void With_the_folder_guard_off_they_both_launch()
    {
        var evaluation = Evaluate(
            [Ready(1040, TaskSchedule.Now), Ready(1041, TaskSchedule.Now)],
            Policy() with { PreventConcurrentWorkingDir = false });

        evaluation.Launch.Should().HaveCount(2);
    }

    [Fact]
    public void An_unfinished_dependency_holds()
    {
        var prerequisite = Ready(1039, TaskSchedule.Manual);
        prerequisite.WorkingDir = "/dev/other";

        var card = Ready(1040, TaskSchedule.Now);
        card.DependsOn.Add(prerequisite.Id);

        var hold = Hold(Evaluate([prerequisite, card]), card);

        hold.Reason.Should().Be(LaunchHold.Dependency);
        hold.Blocker.Should().Be(prerequisite);
    }

    [Fact]
    public void The_cap_holds_the_rest_and_reports_the_figures()
    {
        var running = Ready(1038, TaskSchedule.Manual);
        running.Column = BoardColumn.Executing;
        running.Badge = Badge.Running;
        running.WorkingDir = "/dev/one";

        var card = Ready(1040, TaskSchedule.Now);
        card.WorkingDir = "/dev/two";

        var hold = Hold(Evaluate([running, card], Policy() with { MaxConcurrent = 1 }), card);

        hold.Reason.Should().Be(LaunchHold.Slot);
        hold.Used.Should().Be(1);
        hold.Cap.Should().Be(1);
    }

    // Each launch takes a slot, so the pass has to judge the next card against the board as it will
    // then be rather than as it was when the pass started.
    [Fact]
    public void A_launch_consumes_the_slot_it_takes()
    {
        var first = Ready(1040, TaskSchedule.Now);
        first.WorkingDir = "/dev/one";

        var second = Ready(1041, TaskSchedule.Now);
        second.WorkingDir = "/dev/two";

        var evaluation = Evaluate([first, second], Policy() with { MaxConcurrent = 1 });

        evaluation.Launch.Should().ContainSingle().Which.Should().Be(first);
        Hold(evaluation, second).Reason.Should().Be(LaunchHold.Slot);
    }

    [Fact]
    public void Precedence_runs_most_durable_first()
    {
        var running = Ready(1038, TaskSchedule.Manual);
        running.Column = BoardColumn.Executing;
        running.Badge = Badge.Running;

        // Everything is in the way at once: the folder, the cap, and a spent quota.
        var spent = new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 100, Now.AddHours(2))],
            Now,
            false,
            null);

        var card = Ready(1040, TaskSchedule.Now);

        var evaluation = Evaluate(
            [running, card],
            Policy() with { MaxConcurrent = 1 },
            _ => spent);

        Hold(evaluation, card).Reason.Should().Be(LaunchHold.UsageLimit);
    }

    [Fact]
    public void The_queue_is_first_in_first_out_by_number()
    {
        var later = Ready(1041, TaskSchedule.Now);
        later.WorkingDir = "/dev/two";

        var earlier = Ready(1040, TaskSchedule.Now);
        earlier.WorkingDir = "/dev/one";

        Evaluate([later, earlier]).Launch.Select(card => card.Number)
            .Should().Equal(1040, 1041);
    }

    // A card that has been waiting since it was created should not be jumped by one that has only
    // just come due.
    [Fact]
    public void An_undated_card_goes_before_a_dated_one()
    {
        var dated = Ready(1039, TaskSchedule.SpecificDateTime);
        dated.ScheduledFor = Now.AddMinutes(-1);
        dated.WorkingDir = "/dev/one";

        var immediate = Ready(1041, TaskSchedule.Now);
        immediate.WorkingDir = "/dev/two";

        Evaluate([dated, immediate]).Launch.Select(card => card.Number)
            .Should().Equal(1041, 1039);
    }

    [Fact]
    public void A_window_card_is_armed_against_the_reading_and_not_launched_yet()
    {
        var session = new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 30, Now.AddHours(2), TimeSpan.FromHours(5))],
            Now,
            false,
            null);

        var card = Ready(1040, TaskSchedule.NextWindow);

        var evaluation = Evaluate([card], usage: _ => session);

        evaluation.Arm.Should().ContainKey(card.Id).WhoseValue.Should().Be(Now.AddHours(2));
        evaluation.Launch.Should().BeEmpty();
        evaluation.Holds.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_outside_ready_is_ever_launched()
    {
        Card[] cards =
        [
            At(1040, BoardColumn.Preparing),
            At(1041, BoardColumn.Executing),
            At(1042, BoardColumn.YourTurn),
            At(1043, BoardColumn.Completed),
        ];

        var evaluation = Evaluate(cards, Policy() with { PreventConcurrentWorkingDir = false });

        evaluation.Launch.Should().BeEmpty();
        evaluation.Holds.Should().BeEmpty();
    }

    [Fact]
    public void An_archived_ready_card_is_not_queued()
    {
        var card = Ready(1040, TaskSchedule.Now);
        card.ArchivedAt = Now;

        Evaluate([card]).Launch.Should().BeEmpty();
    }

    private static ReadyHold Hold(QueueEvaluation evaluation, Card card)
    {
        evaluation.Holds.Should().ContainKey(card.Id);

        return evaluation.Holds[card.Id];
    }

    private static QueueEvaluation Evaluate(
        IReadOnlyList<Card> cards,
        QueuePolicy? policy = null,
        Func<AgentType, AgentUsage?>? usage = null)
        => LaunchQueue.Evaluate(
            cards,
            policy ?? Policy(),
            usage ?? (_ => null),
            new PassThroughDirectories(),
            Now);

    private static QueuePolicy Policy(bool paused = false) => new(
        MaxConcurrent: 5,
        AutoExecutionPaused: paused,
        PreventConcurrentWorkingDir: true,
        EnabledAgents: new HashSet<AgentType> { AgentType.ClaudeCode, AgentType.Codex });

    private static Card Ready(int number, TaskSchedule schedule)
    {
        var card = At(number, BoardColumn.Ready);
        card.Schedule = schedule;

        return card;
    }

    private static Card At(int number, BoardColumn column) => new()
    {
        Number = number,
        Title = $"Task {number}",
        Column = column,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Now,
    };

    private sealed class PassThroughDirectories : IWorkingDirectories
    {
        public string Home => "/home/act";

        public string Resolve(string workingDir) => workingDir.Trim().Replace('\\', '/');

        public bool Exists(string workingDir) => true;

        public void Create(string workingDir) { }

        public PathCheck Check(string workingDir) => PathCheck.Found(Resolve(workingDir));

        public DirectoryListing List(string? path, bool includeFiles = false) => new(path, null, []);
    }
}
