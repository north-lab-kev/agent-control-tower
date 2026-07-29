namespace Act.Core.Events;

// `Summary` is what the drawer shows: a brief statement of what the agent wants to do.
// Deliberately not a command dump — the UI direction keeps the approve/deny surface simple.
public sealed record PermissionRequested(
    string SessionId,
    DateTimeOffset At,
    string RequestId,
    string Summary)
    : AgentEvent(SessionId, At);
