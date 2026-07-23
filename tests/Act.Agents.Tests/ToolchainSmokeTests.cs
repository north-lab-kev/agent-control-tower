using AwesomeAssertions;

namespace Act.Agents.Tests;

public class ToolchainSmokeTests
{
    [Fact]
    public void Assertions_and_runner_are_wired()
    {
        var sum = 40 + 2;

        sum.Should().Be(42);
    }
}
