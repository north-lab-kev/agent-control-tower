namespace Act.Core.Events;

public sealed record NoActivityElapsed(string SessionId, DateTimeOffset At, TimeSpan Idle)
    : AgentEvent(SessionId, At);
