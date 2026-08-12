using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class ExitPolicyTests
{
    [Fact]
    public void An_idle_board_warns_about_nothing()
    {
        var stakes = ExitPolicy.Assess([], paused: false);

        stakes.Warning.Should().Be(ExitWarning.None);
    }

    [Fact]
    public void An_executing_card_counts_as_running()
    {
        var stakes = ExitPolicy.Assess([Executing()], paused: false);

        stakes.Running.Should().Be(1);
        stakes.Warning.Should().Be(ExitWarning.Running);
    }

    [Fact]
    public void A_card_waiting_on_permission_counts_as_running()
        => ExitPolicy.Assess([YourTurn(Badge.NeedsPermission)], paused: false)
            .Running.Should().Be(1);

    [Theory]
    [InlineData(Badge.ReadyForReview)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.Error)]
    [InlineData(Badge.Killed)]
    public void A_card_whose_turn_has_ended_does_not(Badge badge)
        => ExitPolicy.Assess([YourTurn(badge)], paused: false)
            .Warning.Should().Be(ExitWarning.None);

    [Fact]
    public void Running_cards_are_counted_not_just_noticed()
        => ExitPolicy.Assess([Executing(), Executing(), YourTurn(Badge.NeedsPermission)], paused: false)
            .Running.Should().Be(3);

    [Fact]
    public void A_manual_ready_card_warns_about_nothing()
        => ExitPolicy.Assess([Ready(TaskSchedule.Manual)], paused: false)
            .Warning.Should().Be(ExitWarning.None);

    [Fact]
    public void So_does_a_ready_card_with_no_schedule_at_all()
        => ExitPolicy.Assess([Ready(null)], paused: false)
            .Warning.Should().Be(ExitWarning.None);

    [Theory]
    [InlineData(TaskSchedule.Now)]
    [InlineData(TaskSchedule.NextWindow)]
    [InlineData(TaskSchedule.SpecificDateTime)]
    public void A_ready_card_that_can_launch_on_its_own_warns_about_the_schedule(TaskSchedule schedule)
        => ExitPolicy.Assess([Ready(schedule)], paused: false)
            .Warning.Should().Be(ExitWarning.Scheduled);

    [Fact]
    public void Pausing_auto_execution_silences_the_schedule_warning()
        => ExitPolicy.Assess([Ready(TaskSchedule.Now)], paused: true)
            .Warning.Should().Be(ExitWarning.None);

    [Fact]
    public void But_not_the_running_one()
        => ExitPolicy.Assess([Executing()], paused: true)
            .Warning.Should().Be(ExitWarning.Running);

    [Fact]
    public void Running_and_scheduled_together_warn_about_both()
        => ExitPolicy.Assess([Executing(), Ready(TaskSchedule.Now)], paused: false)
            .Warning.Should().Be(ExitWarning.RunningAndScheduled);

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Completed)]
    public void A_column_the_user_controls_warns_about_nothing(BoardColumn column)
    {
        var card = Executing();
        card.Column = column;

        ExitPolicy.Assess([card], paused: false).Warning.Should().Be(ExitWarning.None);
    }

    [Fact]
    public void An_archived_card_warns_about_nothing()
    {
        var executing = Executing();
        executing.ArchivedAt = DateTimeOffset.UtcNow;

        var scheduled = Ready(TaskSchedule.Now);
        scheduled.ArchivedAt = DateTimeOffset.UtcNow;

        ExitPolicy.Assess([executing, scheduled], paused: false).Warning.Should().Be(ExitWarning.None);
    }

    private static Card Executing() => new()
    {
        Title = "Working",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
    };

    private static Card YourTurn(Badge badge) => new()
    {
        Title = "Waiting",
        Column = BoardColumn.YourTurn,
        Badge = badge,
    };

    private static Card Ready(TaskSchedule? schedule) => new()
    {
        Title = "Queued up",
        Column = BoardColumn.Ready,
        Schedule = schedule,
    };
}
