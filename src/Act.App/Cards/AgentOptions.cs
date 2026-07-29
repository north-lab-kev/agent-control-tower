using Act.Core.Model;

namespace Act.App.Cards;

// Placeholder — `model` and `effort` are agent- and model-specific, and become each
// adapter's responsibility in roadmap steps 6-7. These stand-in lists exist only so the
// new-task modal has something to offer until the adapter seam can supply the real values.
internal static class AgentOptions
{
    public static IReadOnlyList<string> ModelsFor(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => ["opus", "sonnet", "haiku"],
        _ => [],
    };

    public static IReadOnlyList<string> Efforts => ["low", "medium", "high", "xhigh"];
}
