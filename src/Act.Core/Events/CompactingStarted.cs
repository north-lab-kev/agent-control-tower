namespace Act.Core.Events;

public sealed record CompactingStarted(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
