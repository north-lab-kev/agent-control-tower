namespace Act.Core.Events;

public sealed record ProcessExited(string SessionId, DateTimeOffset At, int ExitCode)
    : AgentEvent(SessionId, At);
