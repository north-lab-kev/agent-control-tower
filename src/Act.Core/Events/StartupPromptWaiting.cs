namespace Act.Core.Events;

// The process source's one pre-session observation. Both CLIs open on a directory-trust prompt for
// a working directory they have not seen before, and it blocks *before* the session exists — so it
// is the one waiting prompt no hook can report. Measured against `claude-code` on 2026-07-30: with
// the trust prompt on screen not a single hook fires for as long as it is left there, not even
// `SessionStart`, while a directory the CLI already trusts produces its first hook ~0.8 s after the
// spawn. The absence is therefore the signal, and it is a fact about ACT's own process rather than
// anything read off the terminal.
public sealed record StartupPromptWaiting(string SessionId, DateTimeOffset At)
    : AgentEvent(SessionId, At);
