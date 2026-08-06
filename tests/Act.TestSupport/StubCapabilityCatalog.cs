using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.TestSupport;

// Two agents with deliberately *different* vocabularies, because that is what a cross-agent spawn
// has to survive: a model slug one CLI knows and the other has never heard of. A catalog whose two
// entries answered the same would pass every test a Claude-shaped one could write.
public sealed class StubCapabilityCatalog : IAgentCapabilityCatalog
{
    public const string ClaudeModel = "sonnet";

    public const string CodexModel = "gpt-5.3";

    private static readonly AgentCapabilities Claude = new(
        [
            new AgentModel(ClaudeModel, "Sonnet", ["low", "medium", "high"], "medium", 1_000_000),
            new AgentModel("opus", "Opus", ["low", "medium", "high"], "high", 1_000_000),
        ],
        ClaudeModel,
        [
            PermissionMode.Default,
            PermissionMode.Plan,
            PermissionMode.AcceptEdits,
            PermissionMode.Auto,
            PermissionMode.DontAsk,
            PermissionMode.Bypass,
        ]);

    // Narrower on purpose, and it is the narrowness that does the work: no `acceptEdits`, so an
    // inherited permission mode really can fail to cross.
    private static readonly AgentCapabilities Codex = new(
        [
            new AgentModel(CodexModel, "GPT-5.3", ["low", "high"], "low", 258_000),
        ],
        CodexModel,
        [PermissionMode.Default, PermissionMode.Plan, PermissionMode.Auto, PermissionMode.Bypass]);

    public AgentCapabilities For(AgentType agent)
        => agent is AgentType.Codex ? Codex : Claude;
}
