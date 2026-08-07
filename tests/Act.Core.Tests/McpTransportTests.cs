using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

// Wire constants, pinned because both CLIs are configured from them and a drift is silent: a renamed
// tool simply never appears in the model's list, and a permission id that no longer matches the tool it
// names costs an approval prompt on a card nobody is awake to answer.
public class McpTransportTests
{
    // Three, and only three. The tool list is the agent's whole view of what ACT can be asked for, so a
    // fourth is a capability somebody has to have decided to grant.
    [Fact]
    public void The_server_offers_exactly_the_three_tools_act_decided_on()
        => McpTransport.Tools.Should().BeEquivalentTo(["create_followup", "list_tasks", "get_task"]);

    // Shares the hook listener but not its paths — the route family is what `HookPortGuard` keys on.
    [Fact]
    public void The_route_is_its_own_family_beside_the_hooks()
    {
        McpTransport.Route.Should().Be("/mcp");
        McpTransport.Route.Should().NotStartWith(HookTransport.RoutePrefix);
    }
}
