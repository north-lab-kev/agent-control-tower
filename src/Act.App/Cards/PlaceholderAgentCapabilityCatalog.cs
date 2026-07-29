using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Cards;

// Placeholder — real capabilities belong to the Claude Code and Codex adapters (roadmap
// step 7). It sits behind the seam so the new-task modal already asks the catalog rather than
// a hard-coded list, and step 7 only has to swap the implementation for one built from the
// registered adapters.
internal sealed class PlaceholderAgentCapabilityCatalog : IAgentCapabilityCatalog
{
    private static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh"];

    private static readonly IReadOnlySet<PermissionMode> PermissionModes =
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>());

    public AgentCapabilities For(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => new AgentCapabilities(
            ["opus", "sonnet", "haiku"],
            "sonnet",
            Efforts,
            PermissionModes),
        _ => new AgentCapabilities([], null, Efforts, PermissionModes),
    };
}
