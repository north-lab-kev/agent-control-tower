using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.Codex;

// Transcribed from `codex debug models` on codex-cli 0.146.0-alpha.3.1. Note each model
// carries a different effort ladder — that asymmetry is why `AgentCapabilities` models effort
// per model rather than per agent. `codex-auto-review` is omitted: the catalog marks it
// `visibility: hide`, so it is not a model a user picks.
//
// PENDING — this is a snapshot of a catalog the CLI can render at runtime
// (`codex debug models` emits JSON). Reading it live would keep ACT honest across CLI
// upgrades; that is a step-8 job, once ACT is allowed to shell out to the binary at all.
public static class CodexCapabilities
{
    private static readonly IReadOnlyList<string> ThroughXHigh = ["low", "medium", "high", "xhigh"];

    private static readonly IReadOnlyList<string> ThroughMax = ["low", "medium", "high", "xhigh", "max"];

    private static readonly IReadOnlyList<string> ThroughUltra =
        ["low", "medium", "high", "xhigh", "max", "ultra"];

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("gpt-5.6-terra", "GPT-5.6 Terra", ThroughUltra, "medium"),
            new AgentModel("gpt-5.6-luna", "GPT-5.6 Luna", ThroughMax, "medium"),
            new AgentModel("gpt-5.5", "GPT-5.5", ThroughXHigh, "medium"),
            new AgentModel("gpt-5.4-mini", "GPT-5.4 Mini", ThroughXHigh, "medium"),
        ],
        "gpt-5.6-terra",
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()),
        DesktopHandoff: false);
}
