using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class UsageWakeTests
{
    [Theory]
    [InlineData(UsageAvailability.NotSignedIn)]
    [InlineData(UsageAvailability.Expired)]
    [InlineData(UsageAvailability.SignInRequired)]
    [InlineData(UsageAvailability.Unauthorized)]
    public void An_outcome_a_new_credential_cures_wakes_on_a_file_change(UsageAvailability availability)
        => UsageWake.Wakes(availability).Should().BeTrue();

    [Theory]
    [InlineData(UsageAvailability.Available)]
    [InlineData(UsageAvailability.Off)]
    [InlineData(UsageAvailability.Unreachable)]
    [InlineData(UsageAvailability.Failed)]
    public void An_outcome_a_new_credential_cannot_cure_does_not_wake(UsageAvailability availability)
        => UsageWake.Wakes(availability).Should().BeFalse();

    [Fact]
    public void The_floor_is_the_shortest_gap_the_polling_contract_allows()
        => UsageWake.Floor.Should().Be(TimeSpan.FromSeconds(60));
}
