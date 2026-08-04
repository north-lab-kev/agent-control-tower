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
}
