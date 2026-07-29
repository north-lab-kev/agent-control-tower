namespace Act.Core.Events;

public sealed record CompactingFinished(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
