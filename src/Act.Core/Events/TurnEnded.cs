namespace Act.Core.Events;

public sealed record TurnEnded(string SessionId, DateTimeOffset At) : AgentEvent(SessionId, At);
