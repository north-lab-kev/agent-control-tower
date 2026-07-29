namespace Act.Core.Events;

// An observation, not a request: ACT reports that the TUI is waiting and the user answers
// it there. `Summary` is the brief read-only statement the card and drawer show —
// deliberately not a command dump. `RequestId` exists to de-duplicate repeats of the same
// prompt and to correlate the hook payload, never to answer with.
public sealed record PermissionRequested(
    string SessionId,
    DateTimeOffset At,
    string RequestId,
    string Summary)
    : AgentEvent(SessionId, At);
