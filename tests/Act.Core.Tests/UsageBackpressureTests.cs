using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class UsageBackpressureTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    // The load-bearing one: a probe that cannot read its credentials must not freeze an overnight
    // queue. The CLI gets to be the one that refuses.
    [Fact]
    public void No_reading_is_not_a_limit()
        => Blocking(null).Should().BeNull();

    [Fact]
    public void A_quota_with_room_blocks_nothing()
        => Blocking(Usage(Window(UsageWindowKind.Session, 62, 2))).Should().BeNull();

    [Fact]
    public void An_exhausted_window_is_the_one_to_wait_for()
        => Blocking(Usage(Window(UsageWindowKind.Session, 100, 2)))
            ?.ResetsAt.Should().Be(Now.AddHours(2));

    // ACT polls, so a window past its own reset says nothing about the new one — and a stale 100%
    // would otherwise lock the queue out until the next poll lands.
    [Fact]
    public void A_reading_held_past_its_reset_is_not_a_limit()
        => Blocking(Usage(Window(UsageWindowKind.Session, 100, -1))).Should().BeNull();

    // The ceiling is what stops a task being launched into the last percent of a quota, where it
    // would start, burn the remainder and stop mid-run rather than being refused outright.
    [Fact]
    public void A_window_at_the_ceiling_blocks_before_it_reads_a_hundred()
    {
        var usage = Usage(Window(UsageWindowKind.Session, UsageCeiling.Default, 2));

        Blocking(usage)?.Kind.Should().Be(UsageWindowKind.Session);
        Blocking(Usage(Window(UsageWindowKind.Session, UsageCeiling.Default - 1, 2))).Should().BeNull();
    }

    // How much quota to leave for interactive work is the user's call, so a low ceiling is honoured
    // rather than second-guessed.
    [Fact]
    public void The_ceiling_is_the_users_number()
    {
        var usage = Usage(Window(UsageWindowKind.Session, 30, 2));

        Blocking(usage, ceiling: 25)?.Kind.Should().Be(UsageWindowKind.Session);
        Blocking(usage, ceiling: 35).Should().BeNull();
    }

    // The bounds only keep the number a percentage.
    [Fact]
    public void A_ceiling_out_of_bounds_is_clamped_to_a_percentage()
    {
        Blocking(Usage(Window(UsageWindowKind.Session, 99, 2)), ceiling: 500).Should().BeNull();

        Blocking(Usage(Window(UsageWindowKind.Session, 100, 2)), ceiling: 500)
            ?.Kind.Should().Be(UsageWindowKind.Session);

        Blocking(Usage(Window(UsageWindowKind.Session, 1, 2)), ceiling: -5)
            ?.Kind.Should().Be(UsageWindowKind.Session);
    }

    // Launching at the 5-hour boundary against a spent weekly quota would only fail again.
    [Fact]
    public void With_both_spent_the_later_reset_wins()
    {
        var usage = Usage(
            Window(UsageWindowKind.Session, 100, 2),
            Window(UsageWindowKind.Weekly, 100, 50));

        Blocking(usage)?.Kind.Should().Be(UsageWindowKind.Weekly);
    }

    // The account-level flag does not say which window, so the soonest re-check is the answer:
    // being wrong costs one pass rather than days held against a weekly boundary.
    [Fact]
    public void The_account_flag_alone_waits_for_the_soonest_reset()
    {
        var usage = Usage(
            limitReached: true,
            Window(UsageWindowKind.Session, 40, 2),
            Window(UsageWindowKind.Weekly, 55, 50));

        Blocking(usage)?.Kind.Should().Be(UsageWindowKind.Session);
    }

    [Fact]
    public void A_named_exhaustion_outranks_the_flag()
    {
        var usage = Usage(
            limitReached: true,
            Window(UsageWindowKind.Session, 40, 2),
            Window(UsageWindowKind.Weekly, 100, 50));

        Blocking(usage)?.Kind.Should().Be(UsageWindowKind.Weekly);
    }

    // What this returns is an instant to wait for, so a window that has not started cannot be the
    // answer — the queue launches and the CLI gets to refuse, as it does for no reading at all.
    [Fact]
    public void A_window_that_has_not_started_never_blocks()
    {
        var usage = Usage(limitReached: true, new UsageWindow(UsageWindowKind.Session, 100, null));

        Blocking(usage).Should().BeNull();
    }

    [Fact]
    public void A_started_window_is_preferred_over_one_that_has_not()
    {
        var usage = Usage(
            limitReached: true,
            new UsageWindow(UsageWindowKind.Session, 0, null),
            Window(UsageWindowKind.Weekly, 100, 50));

        Blocking(usage)?.Kind.Should().Be(UsageWindowKind.Weekly);
    }

    private static UsageWindow? Blocking(AgentUsage? usage, int ceiling = UsageCeiling.Default)
        => UsageBackpressure.Blocking(usage, Now, ceiling);

    private static UsageWindow Window(UsageWindowKind kind, int percent, int hours)
        => new(kind, percent, Now.AddHours(hours));

    private static AgentUsage Usage(params UsageWindow[] windows)
        => Usage(false, windows);

    private static AgentUsage Usage(bool limitReached, params UsageWindow[] windows)
        => new(AgentType.ClaudeCode, windows, Now, limitReached, null);
}
