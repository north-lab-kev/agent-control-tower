using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class ScheduleArmingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TaskSchedule.NextWindow)]
    [InlineData(TaskSchedule.WindowAfterNext)]
    public void A_window_schedule_needs_arming(TaskSchedule schedule)
        => ScheduleArming.NeedsArming(Ready(schedule)).Should().BeTrue();

    [Theory]
    [InlineData(TaskSchedule.Manual)]
    [InlineData(TaskSchedule.Now)]
    [InlineData(TaskSchedule.SpecificDateTime)]
    public void Nothing_else_does(TaskSchedule schedule)
        => ScheduleArming.NeedsArming(Ready(schedule)).Should().BeFalse();

    [Fact]
    public void A_card_outside_ready_is_never_armed()
    {
        var card = Ready(TaskSchedule.NextWindow);
        card.Column = BoardColumn.Preparing;

        ScheduleArming.NeedsArming(card).Should().BeFalse();
    }

    [Fact]
    public void An_armed_card_is_not_armed_again()
    {
        var card = Ready(TaskSchedule.NextWindow);
        card.EligibleAt = Now;

        ScheduleArming.NeedsArming(card).Should().BeFalse();
    }

    [Fact]
    public void Next_window_arms_to_the_reset_the_reading_reports()
        => ScheduleArming.Arm(Ready(TaskSchedule.NextWindow), Session())
            .Should().Be(Now.AddHours(2));

    // The whole reason `UsageWindow` carries its length: one window further out is measured, not
    // guessed at five hours.
    [Fact]
    public void Window_after_next_arms_one_declared_length_beyond_it()
        => ScheduleArming.Arm(Ready(TaskSchedule.WindowAfterNext), Session())
            .Should().Be(Now.AddHours(2).AddHours(5));

    [Fact]
    public void A_window_that_declares_no_length_falls_back_to_the_nominal_one()
    {
        var weekly = new UsageWindow(UsageWindowKind.Weekly, 20, Now.AddDays(1));

        ScheduleArming.Arm(Ready(TaskSchedule.WindowAfterNext), weekly)
            .Should().Be(Now.AddDays(1).AddDays(7));
    }

    // Unlike backpressure, where an unreadable quota launches anyway: a boundary ACT cannot see is
    // not something to guess at, and the intent chip already says the card is waiting for one.
    [Fact]
    public void With_no_reading_there_is_nothing_to_arm_against()
        => ScheduleArming.Arm(Ready(TaskSchedule.NextWindow), null).Should().BeNull();

    [Fact]
    public void Now_is_due_immediately()
        => ScheduleArming.IsDue(Ready(TaskSchedule.Now), Now).Should().BeTrue();

    [Fact]
    public void Manual_is_never_due()
        => ScheduleArming.IsDue(Ready(TaskSchedule.Manual), Now.AddYears(1)).Should().BeFalse();

    [Fact]
    public void A_card_with_no_schedule_at_all_is_never_due()
    {
        var card = Ready(TaskSchedule.Manual);
        card.Schedule = null;

        ScheduleArming.IsDue(card, Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void A_specific_time_is_due_from_the_instant_it_names(int minutes, bool due)
    {
        var card = Ready(TaskSchedule.SpecificDateTime);
        card.ScheduledFor = Now.AddMinutes(minutes);

        ScheduleArming.IsDue(card, Now).Should().Be(due);
    }

    [Fact]
    public void An_unarmed_window_card_is_not_due_however_long_it_waits()
        => ScheduleArming.IsDue(Ready(TaskSchedule.NextWindow), Now.AddDays(30)).Should().BeFalse();

    [Fact]
    public void An_armed_window_card_is_due_at_its_instant()
    {
        var card = Ready(TaskSchedule.NextWindow);
        card.EligibleAt = Now.AddHours(2);

        ScheduleArming.IsDue(card, Now.AddHours(1)).Should().BeFalse();
        ScheduleArming.IsDue(card, Now.AddHours(2)).Should().BeTrue();
    }

    private static UsageWindow Session()
        => new(UsageWindowKind.Session, 40, Now.AddHours(2), TimeSpan.FromHours(5));

    private static Card Ready(TaskSchedule schedule) => new()
    {
        Title = "The next task",
        Column = BoardColumn.Ready,
        Schedule = schedule,
    };
}
