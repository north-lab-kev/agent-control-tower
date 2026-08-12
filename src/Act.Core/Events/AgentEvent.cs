namespace Act.Core.Events;

// The whole vocabulary the rules engine consumes. Every ingestion source — control stream,
// HTTP hook, file watcher, process supervision — normalizes into one of these, so nothing
// downstream ever learns how an event arrived.
public abstract record AgentEvent(string SessionId, DateTimeOffset At);
