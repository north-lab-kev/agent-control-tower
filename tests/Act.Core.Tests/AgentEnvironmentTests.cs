using Act.Core.Abstractions;
using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class AgentEnvironmentTests
{
    [Fact]
    public void A_session_inherits_the_environment_with_the_agents_scrub_applied()
    {
        var environment = AgentEnvironment.For(Guid.NewGuid(), new Dictionary<string, string>(), Removing("PATH"));

        environment.Should().NotContainKey("PATH");
        environment.Should().ContainKey("TERM");
    }

    [Fact]
    public void A_query_inherits_the_environment_with_the_agents_scrub_applied()
        => AgentEnvironment.ForQuery(new Dictionary<string, string>(), Removing("PATH"))
            .Should().NotContainKey("PATH");

    // The scrub runs before the overrides, which is what leaves a user a way to pass one of the
    // variables their agent otherwise removes.
    [Fact]
    public void An_override_still_wins_over_the_scrub()
    {
        var overrides = new Dictionary<string, string> { ["SCRUBBED"] = "kept" };

        AgentEnvironment.For(Guid.NewGuid(), overrides, Removing("SCRUBBED"))["SCRUBBED"]
            .Should().Be("kept");

        AgentEnvironment.ForQuery(overrides, Removing("SCRUBBED"))["SCRUBBED"]
            .Should().Be("kept");
    }

    // ACT's own variables are not the agent's to remove: the scrub sees the inherited environment
    // alone, so a scrub that took everything could not cost a launch its correlation handle.
    [Fact]
    public void The_scrub_cannot_reach_what_ACT_adds()
    {
        var taskId = Guid.NewGuid();

        var environment = AgentEnvironment.For(
            taskId,
            new Dictionary<string, string>(),
            Removing(AgentEnvironment.ActTaskId, AgentEnvironment.ActHookToken, "TERM"),
            hookToken: "token");

        environment[AgentEnvironment.ActTaskId].Should().Be(taskId.ToString("d"));
        environment[AgentEnvironment.ActHookToken].Should().Be("token");
        environment["TERM"].Should().Be("xterm-256color");
    }

    [Fact]
    public void An_agent_with_nothing_to_subtract_inherits_the_environment_whole()
        => AgentEnvironment.ForQuery(new Dictionary<string, string>(), scrub: null)
            .Should().ContainKey("PATH");

    private static IEnvironmentScrub Removing(params string[] keys) => new StubScrub(keys);

    private sealed class StubScrub(string[] keys) : IEnvironmentScrub
    {
        public void Apply(IDictionary<string, string> environment)
        {
            foreach (var key in keys)
                environment.Remove(key);
        }
    }
}
