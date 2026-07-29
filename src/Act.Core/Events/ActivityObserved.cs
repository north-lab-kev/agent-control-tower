namespace Act.Core.Events;

public sealed record ActivityObserved(
    string SessionId,
    DateTimeOffset At,
    string? ToolName = null)
    : AgentEvent(SessionId, At);
