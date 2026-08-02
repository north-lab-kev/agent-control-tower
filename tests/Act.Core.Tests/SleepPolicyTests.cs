using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class SleepPolicyTests
{
    [Fact]
    public void The_setting_off_means_no_hold_whatever_is_running()
        => SleepPolicy.ShouldHold([Executing()], keepAwake: false, paused: false).Should().BeFalse();

    // The whole point of the change: ACT being open is not a reason to keep a laptop up.
    [Fact]
    public void An_idle_board_is_left_to_sleep()
        => SleepPolicy.ShouldHold([Ready(TaskSchedule.Manual)], keepAwake: true, paused: false)
            .Should().BeFalse();

    [Fact]
    public void Live_work_is_held_for()
        => SleepPolicy.ShouldHold([Executing()], keepAwake: true, paused: false).Should().BeTrue();

    [Theory]
    [InlineData(TaskSchedule.Now)]
    [InlineData(TaskSchedule.NextWindow)]
    [InlineData(TaskSchedule.SpecificDateTime)]
    public void So_is_a_ready_card_that_can_launch_on_its_own(TaskSchedule schedule)
        => SleepPolicy.ShouldHold([Ready(schedule)], keepAwake: true, paused: false).Should().BeTrue();

    // Nothing can start while the switch is off, so there is nothing to stay awake for.
    [Fact]
    public void Pausing_releases_the_hold_a_scheduled_card_was_taking()
        => SleepPolicy.ShouldHold([Ready(TaskSchedule.Now)], keepAwake: true, paused: true)
            .Should().BeFalse();

    [Fact]
    public void But_not_the_one_live_work_is_taking()
        => SleepPolicy.ShouldHold([Executing()], keepAwake: true, paused: true).Should().BeTrue();

    // Deliberately *not* the line the cap draws — an `error` card occupies a slot, because it is
    // work the user still owes an answer to, but it has no process, and keeping a laptop up all
    // night for a session that died at 3am protects nothing.
    [Theory]
    [InlineData(Badge.ReadyForReview)]
    [InlineData(Badge.Error)]
    [InlineData(Badge.Killed)]
    public void A_card_whose_process_is_gone_is_not_worth_a_hold(Badge badge)
    {
        var card = Executing();
        card.Column = BoardColumn.YourTurn;
        card.Badge = badge;

        SleepPolicy.ShouldHold([card], keepAwake: true, paused: false).Should().BeFalse();
    }

    [Fact]
    public void An_archived_scheduled_card_holds_nothing()
    {
        var card = Ready(TaskSchedule.Now);
        card.ArchivedAt = DateTimeOffset.UtcNow;

        SleepPolicy.ShouldHold([card], keepAwake: true, paused: false).Should().BeFalse();
    }

    private static Card Executing() => new()
    {
        Title = "Working",
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
    };

    private static Card Ready(TaskSchedule schedule) => new()
    {
        Title = "Queued up",
        Column = BoardColumn.Ready,
        Schedule = schedule,
    };
}
