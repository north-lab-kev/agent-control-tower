namespace Act.Core.Events;

// The process source's one pre-session observation. Both CLIs open on a directory-trust prompt for
// a working directory they have not seen before, and it blocks *before* the session exists — so it
// is the one waiting prompt no hook can report, and silence is what reports it. A fact about ACT's
// own process rather than anything read off the terminal; `docs/design-notes.md` has the
// measurement behind the grace period.
public sealed record StartupPromptWaiting(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
