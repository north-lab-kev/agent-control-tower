namespace Act.Core.Events;

public sealed record SessionEnded(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
