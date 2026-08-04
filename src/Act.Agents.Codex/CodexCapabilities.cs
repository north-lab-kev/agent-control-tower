using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.Codex;

// A pinned snapshot of `codex debug models` on codex-cli 0.146.0-alpha.3.1, and a thing to
// re-check on a version bump. Each model carries a different effort ladder — that asymmetry is why
// `AgentCapabilities` models effort per model rather than per agent.
//
// Pinned rather than read at runtime on purpose; `docs/design-notes.md` says why the CLI's own
// cache is not a contract ACT can lean on.
public static class CodexCapabilities
{
    private static readonly IReadOnlyList<string> ThroughXHigh = ["low", "medium", "high", "xhigh"];

    private static readonly IReadOnlyList<string> ThroughMax = ["low", "medium", "high", "xhigh", "max"];

    private static readonly IReadOnlyList<string> ThroughUltra =
        ["low", "medium", "high", "xhigh", "max", "ultra"];

    // Five, not six — `Auto` is deliberately absent, because Codex has no classifier tier and
    // offering one it does not have put two identical choices in the task form. A card still
    // carrying `Auto` from before is refused at launch rather than quietly run as something else.
    private static readonly IReadOnlyList<PermissionMode> Modes =
    [
        PermissionMode.Default,
        PermissionMode.Plan,
        PermissionMode.AcceptEdits,
        PermissionMode.DontAsk,
        PermissionMode.Bypass,
    ];

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("gpt-5.6-terra", "GPT-5.6 Terra", ThroughUltra, "medium"),
            new AgentModel("gpt-5.6-luna", "GPT-5.6 Luna", ThroughMax, "medium"),
            new AgentModel("gpt-5.5", "GPT-5.5", ThroughXHigh, "medium"),
            new AgentModel("gpt-5.4-mini", "GPT-5.4 Mini", ThroughXHigh, "medium"),
        ],
        "gpt-5.6-terra",
        Modes,
        DesktopHandoff: false,
        UtilityModel: "gpt-5.4-mini");
}
