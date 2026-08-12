using Act.Core.Events;

namespace Act.Core.Abstractions;

// An unrecognised hook event normalizes to nothing rather than to an error: ACT observes what it
// understands and lets a CLI grow events it has never heard of. `SessionId` is what the payload
// claimed, which for a self-minting agent is how the binding arrives; `TranscriptPath` is the same
// kind of thing — a per-payload observation rather than an event, and what starts the transcript
// tail.
public sealed record HookNormalization(
    string? SessionId,
    IReadOnlyList<AgentEvent> Events,
    string? TranscriptPath = null)
{
    public static readonly HookNormalization None = new(null, []);
}
