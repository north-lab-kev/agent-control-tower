using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class UsageRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_expired_token_is_the_one_outcome_a_nudge_answers()
        => UsageRefresh.Answers(UsageAvailability.Expired).Should().BeTrue();

    // `NotSignedIn` needs an interactive login and `Unauthorized` cannot be told apart from a revoked
    // account, so neither is something running the CLI would fix. `SignInRequired` is the sharpest of
    // the three: the refresh token's own expiry has passed, so ACT knows for a fact that a nudge cannot
    // help and declines to spawn the process at all.
    [Theory]
    [InlineData(UsageAvailability.Available)]
    [InlineData(UsageAvailability.Off)]
    [InlineData(UsageAvailability.NotSignedIn)]
    [InlineData(UsageAvailability.SignInRequired)]
    [InlineData(UsageAvailability.Unauthorized)]
    [InlineData(UsageAvailability.Unreachable)]
    [InlineData(UsageAvailability.Failed)]
    public void No_other_outcome_is_worth_spawning_a_process_for(UsageAvailability availability)
        => UsageRefresh.Answers(availability).Should().BeFalse();

    [Fact]
    public void A_first_attempt_is_always_due()
        => UsageRefresh.Due(Now, null, UsageRefresh.Cooldown).Should().BeTrue();

    [Fact]
    public void A_second_attempt_inside_the_cooldown_is_not_due()
        => UsageRefresh.Due(Now, Now - TimeSpan.FromMinutes(5), UsageRefresh.Cooldown).Should().BeFalse();

    [Fact]
    public void An_attempt_once_the_cooldown_has_passed_is_due_again()
        => UsageRefresh.Due(Now, Now - TimeSpan.FromMinutes(20), UsageRefresh.Cooldown).Should().BeTrue();

    // The boundary counts as due: a cooldown of exactly n minutes means one attempt every n minutes,
    // not one every n minutes plus a poll interval.
    [Fact]
    public void The_cooldown_boundary_itself_is_due()
        => UsageRefresh.Due(Now, Now - UsageRefresh.Cooldown, UsageRefresh.Cooldown).Should().BeTrue();

    // The nudge costs a measured ~5,000 tokens, so one that left the token expired has spent the
    // user's money for nothing — and the likeliest cause, a refresh token revoked while its stated
    // expiry is still in the future, never heals. Spending is what backs off here.
    [Fact]
    public void A_nudge_that_left_the_token_expired_counts_as_a_failure()
        => UsageRefresh.Count(UsageAvailability.Expired, 0).Should().Be(1);

    [Fact]
    public void A_nudge_that_restored_the_reading_clears_the_count()
        => UsageRefresh.Count(UsageAvailability.Available, 4).Should().Be(0);

    // A refresh token that lapsed outright is not a failed nudge — no nudge is attempted at all, and
    // the count has nothing to say about a state that is waiting on the user.
    [Fact]
    public void A_lapsed_login_clears_the_count_rather_than_growing_it()
        => UsageRefresh.Count(UsageAvailability.SignInRequired, 3).Should().Be(0);

    [Fact]
    public void Each_wasted_nudge_doubles_the_wait()
    {
        UsageRefresh.Delay(UsageRefresh.Cooldown, 0).Should().Be(UsageRefresh.Cooldown);
        UsageRefresh.Delay(UsageRefresh.Cooldown, 1).Should().Be(TimeSpan.FromMinutes(30));
        UsageRefresh.Delay(UsageRefresh.Cooldown, 2).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void The_wait_never_passes_the_ceiling()
        => UsageRefresh.Delay(UsageRefresh.Cooldown, UsageBackoff.MostDoublings)
            .Should().Be(UsageRefresh.Ceiling);

    [Fact]
    public void A_cooldown_slower_than_the_ceiling_is_never_shortened_by_a_failure()
    {
        var daily = TimeSpan.FromHours(6);

        UsageRefresh.Delay(daily, 3).Should().Be(daily);
    }
}
