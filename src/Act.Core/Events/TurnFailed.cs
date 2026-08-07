namespace Act.Core.Events;

// A turn the agent itself reports as failed, while its process stays alive and would exit zero — an
// api error, a model the account cannot use, a request refused. `ProcessExited` cannot see it and a
// bare `TurnEnded` would offer the failure up for review as if it were work. `Reason` is what the CLI
// said, verbatim and read-only, like every other message ACT carries.
public sealed record TurnFailed(string SessionId, DateTimeOffset At, string Reason)
    : AgentEvent(SessionId, At);
