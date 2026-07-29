namespace Act.Core.Events;

// The outcome comes from the status file the preamble asks the agent to write. `Unknown`
// is the missing-or-malformed case, which the rules engine treats as ready for review
// rather than trapping a finished task in limbo.
public sealed record TurnEnded(
    string SessionId,
    DateTimeOffset At,
    TurnOutcome Outcome,
    string? Question = null)
    : AgentEvent(SessionId, At);
