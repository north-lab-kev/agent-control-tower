using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Hard-coded rather than probed: the CLI has no machine-readable model catalog, so this list
// is a pinned fact about a CLI version and a thing to re-check on a bump. Every listed model
// takes the same effort ladder, unlike Codex's per-model one.
public static class ClaudeCodeCapabilities
{
    private static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh"];

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("opus", "Opus 5", Efforts, "high"),
            new AgentModel("sonnet", "Sonnet 5", Efforts, "medium"),
            new AgentModel("haiku", "Haiku 4.5", Efforts, "low"),
        ],
        "sonnet",
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()),
        DesktopHandoff: true);
}
