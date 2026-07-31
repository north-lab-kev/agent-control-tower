using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class UsageWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 4, 0, 0, TimeSpan.Zero);

    // The window set is a property of the plan, not of the agent: a free Codex login reports one
    // 30-day window and a paid one reports 5-hour plus weekly, so the length the server declares is
    // what names the window.
    [Theory]
    [InlineData(300, UsageWindowKind.Session)]
    [InlineData(10080, UsageWindowKind.Weekly)]
    [InlineData(43200, UsageWindowKind.Monthly)]
    public void A_window_is_named_by_the_length_the_server_declares(int minutes, UsageWindowKind expected)
    {
        UsageWindow.Classify(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }

    [Fact]
    public void The_boundaries_fall_between_the_three_kinds()
    {
        UsageWindow.Classify(TimeSpan.FromHours(8)).Should().Be(UsageWindowKind.Session);
        UsageWindow.Classify(TimeSpan.FromHours(9)).Should().Be(UsageWindowKind.Weekly);
        UsageWindow.Classify(TimeSpan.FromDays(8)).Should().Be(UsageWindowKind.Weekly);
        UsageWindow.Classify(TimeSpan.FromDays(9)).Should().Be(UsageWindowKind.Monthly);
    }

    [Fact]
    public void A_live_window_reports_what_was_read()
    {
        var window = new UsageWindow(UsageWindowKind.Session, 16, Now.AddHours(3));

        window.HasRolledOver(Now).Should().BeFalse();
        window.PercentAt(Now).Should().Be(16);
    }

    // The trap this exists to close: a reading held past its own reset describes a window that no
    // longer exists. Nothing has run since — ACT polls — so zero is the honest number, and the old
    // percentage is simply wrong.
    [Fact]
    public void A_window_that_has_rolled_over_reports_zero_rather_than_the_stale_percentage()
    {
        var window = new UsageWindow(UsageWindowKind.Session, 16, Now.AddMinutes(-1));

        window.HasRolledOver(Now).Should().BeTrue();
        window.PercentAt(Now).Should().Be(0);
    }

    [Fact]
    public void The_countdown_never_runs_negative()
    {
        var window = new UsageWindow(UsageWindowKind.Weekly, 29, Now.AddDays(-2));

        window.RemainingAt(Now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_countdown_is_the_distance_to_the_reset()
    {
        var window = new UsageWindow(UsageWindowKind.Weekly, 29, Now.AddHours(37));

        window.RemainingAt(Now).Should().Be(TimeSpan.FromHours(37));
    }

    [Theory]
    [InlineData(0, UsagePressure.Normal)]
    [InlineData(74, UsagePressure.Normal)]
    [InlineData(75, UsagePressure.Elevated)]
    [InlineData(89, UsagePressure.Elevated)]
    [InlineData(90, UsagePressure.Critical)]
    [InlineData(100, UsagePressure.Critical)]
    public void Pressure_climbs_with_the_percentage(int percent, UsagePressure expected)
    {
        var window = new UsageWindow(UsageWindowKind.Session, percent, Now.AddHours(3));

        window.PressureAt(Now, false).Should().Be(expected);
    }

    // The vendor saying so outranks the threshold: a plan can refuse work before the
    // percentage ACT was last handed reaches ninety.
    [Fact]
    public void A_reported_limit_is_critical_whatever_the_percentage_says()
    {
        var window = new UsageWindow(UsageWindowKind.Session, 12, Now.AddHours(3));

        window.PressureAt(Now, true).Should().Be(UsagePressure.Critical);
    }

    [Fact]
    public void A_rolled_over_window_outranks_everything_including_a_reported_limit()
    {
        var window = new UsageWindow(UsageWindowKind.Session, 99, Now.AddMinutes(-1));

        window.PressureAt(Now, true).Should().Be(UsagePressure.RolledOver);
    }

    [Fact]
    public void A_reading_can_be_asked_for_one_window_by_kind()
    {
        var usage = new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 16, Now.AddHours(3)),
             new UsageWindow(UsageWindowKind.Weekly, 29, Now.AddDays(1))],
            Now,
            false,
            null);

        usage.Of(UsageWindowKind.Weekly)!.Percent.Should().Be(29);
        usage.Of(UsageWindowKind.Monthly).Should().BeNull();
    }
}
