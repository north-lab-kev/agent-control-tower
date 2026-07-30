using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface IAgentCapabilityCatalog
{
    AgentCapabilities For(AgentType agent);
}
