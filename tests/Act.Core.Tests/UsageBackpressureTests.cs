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
        => UsageBackpressure.Blocking(null, Now).Should().BeNull();

    [Fact]
    public void A_quota_with_room_blocks_nothing()
        => UsageBackpressure.Blocking(Usage(Window(UsageWindowKind.Session, 62, 2)), Now)
            .Should().BeNull();

    [Fact]
    public void An_exhausted_window_is_the_one_to_wait_for()
        => UsageBackpressure.Blocking(Usage(Window(UsageWindowKind.Session, 100, 2)), Now)
            ?.ResetsAt.Should().Be(Now.AddHours(2));

    // ACT polls, so a window past its own reset says nothing about the new one — and a stale 100%
    // would otherwise lock the queue out until the next poll lands.
    [Fact]
    public void A_reading_held_past_its_reset_is_not_a_limit()
        => UsageBackpressure.Blocking(Usage(Window(UsageWindowKind.Session, 100, -1)), Now)
            .Should().BeNull();

    // Launching at the 5-hour boundary against a spent weekly quota would only fail again.
    [Fact]
    public void With_both_spent_the_later_reset_wins()
    {
        var usage = Usage(
            Window(UsageWindowKind.Session, 100, 2),
            Window(UsageWindowKind.Weekly, 100, 50));

        UsageBackpressure.Blocking(usage, Now)?.Kind.Should().Be(UsageWindowKind.Weekly);
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

        UsageBackpressure.Blocking(usage, Now)?.Kind.Should().Be(UsageWindowKind.Session);
    }

    [Fact]
    public void A_named_exhaustion_outranks_the_flag()
    {
        var usage = Usage(
            limitReached: true,
            Window(UsageWindowKind.Session, 40, 2),
            Window(UsageWindowKind.Weekly, 100, 50));

        UsageBackpressure.Blocking(usage, Now)?.Kind.Should().Be(UsageWindowKind.Weekly);
    }

    private static UsageWindow Window(UsageWindowKind kind, int percent, int hours)
        => new(kind, percent, Now.AddHours(hours));

    private static AgentUsage Usage(params UsageWindow[] windows)
        => Usage(false, windows);

    private static AgentUsage Usage(bool limitReached, params UsageWindow[] windows)
        => new(AgentType.ClaudeCode, windows, Now, limitReached, null);
}
