namespace Act.Core.Events;

// Read-only, like PermissionRequested: the question is shown on the card, and answered by
// the user in the session's terminal. ACT carries no answer channel.
public sealed record QuestionAsked(
    string SessionId,
    DateTimeOffset At,
    string RequestId,
    string Question)
    : AgentEvent(SessionId, At);
