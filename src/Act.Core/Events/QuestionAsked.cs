namespace Act.Core.Events;

public sealed record QuestionAsked(
    string SessionId,
    DateTimeOffset At,
    string RequestId,
    string Question)
    : AgentEvent(SessionId, At);
