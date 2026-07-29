using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Cards;

// Capabilities come from the registered adapters themselves, so the new-task modal cannot
// offer a model or effort that would be rejected at launch. An agent with no adapter
// registered reports nothing, which is what makes its dropdowns come up empty rather than
// promising something ACT cannot start.
internal sealed class AgentCapabilityCatalog(IEnumerable<IAgentAdapter> adapters) : IAgentCapabilityCatalog
{
    private static readonly AgentCapabilities None = new([], null, new HashSet<PermissionMode>());

    private readonly IReadOnlyDictionary<AgentType, AgentCapabilities> byAgent =
        adapters.ToDictionary(adapter => adapter.Agent, adapter => adapter.Capabilities);

    public AgentCapabilities For(AgentType agent)
        => byAgent.TryGetValue(agent, out var capabilities) ? capabilities : None;
}
