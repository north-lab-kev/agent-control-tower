namespace Act.Core.Events;

public sealed record FollowUpsWritten(
    string SessionId,
    DateTimeOffset At,
    IReadOnlyList<string> Files)
    : AgentEvent(SessionId, At);
