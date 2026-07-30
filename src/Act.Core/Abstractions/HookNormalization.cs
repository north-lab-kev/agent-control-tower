using Act.Core.Events;

namespace Act.Core.Abstractions;

// An unrecognised hook event normalizes to nothing rather than to an error: ACT observes what it
// understands and lets a CLI grow events it has never heard of. `SessionId` is what the payload
// claimed, which for a self-minting agent is how the binding arrives.
public sealed record HookNormalization(string? SessionId, IReadOnlyList<AgentEvent> Events)
{
    public static readonly HookNormalization None = new(null, []);
}
