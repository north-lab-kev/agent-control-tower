using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class UsageBackoffTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(UsageAvailability.Unauthorized)]
    [InlineData(UsageAvailability.Unreachable)]
    [InlineData(UsageAvailability.Failed)]
    public void An_outcome_that_cost_a_request_backs_off(UsageAvailability availability)
        => UsageBackoff.Backs(availability).Should().BeTrue();

    [Theory]
    [InlineData(UsageAvailability.Available)]
    [InlineData(UsageAvailability.Off)]
    [InlineData(UsageAvailability.NotSignedIn)]
    [InlineData(UsageAvailability.Expired)]
    public void An_outcome_that_spent_no_request_keeps_the_base_interval(UsageAvailability availability)
    {
        UsageBackoff.Backs(availability).Should().BeFalse();
        UsageBackoff.Delay(Poll, UsageBackoff.Count(availability, 3)).Should().Be(Poll);
    }

    [Fact]
    public void A_success_clears_the_failures_that_came_before_it()
        => UsageBackoff.Count(UsageAvailability.Available, 4).Should().Be(0);

    [Fact]
    public void Each_consecutive_failure_doubles_the_wait()
    {
        UsageBackoff.Delay(Poll, 1).Should().Be(TimeSpan.FromMinutes(10));
        UsageBackoff.Delay(Poll, 2).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void The_wait_never_passes_the_ceiling()
        => UsageBackoff.Delay(Poll, UsageBackoff.MostDoublings).Should().Be(UsageBackoff.Ceiling);

    [Fact]
    public void A_poll_slower_than_the_ceiling_is_never_shortened_by_a_failure()
    {
        var hourly = TimeSpan.FromHours(1);

        UsageBackoff.Delay(hourly, 3).Should().Be(hourly);
    }

    [Fact]
    public void The_failure_count_stops_growing_at_the_last_doubling()
        => UsageBackoff.Count(UsageAvailability.Failed, UsageBackoff.MostDoublings)
            .Should().Be(UsageBackoff.MostDoublings);
}
