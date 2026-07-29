namespace Act.Core.Events;

// Distinct from `ProcessExited` on purpose: a user-terminated session earns the `killed`
// badge, a crash earns `error`, and only the adapter knows which one it just caused.
public sealed record SessionKilled(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
