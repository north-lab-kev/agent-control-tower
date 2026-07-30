namespace Act.Core.Events;

public sealed record SessionEnriched(
    string SessionId,
    DateTimeOffset At,
    EnrichmentSnapshot Snapshot)
    : AgentEvent(SessionId, At);
