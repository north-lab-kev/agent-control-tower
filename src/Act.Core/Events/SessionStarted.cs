namespace Act.Core.Events;

public sealed record SessionStarted(
    string SessionId,
    DateTimeOffset At,
    string TranscriptPath,
    string WorkingDir)
    : AgentEvent(SessionId, At);
