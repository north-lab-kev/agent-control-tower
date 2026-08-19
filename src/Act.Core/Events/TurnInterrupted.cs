namespace Act.Core.Events;

// The turn the user stopped from the keyboard, and the one turn end no ingestion source reports:
// measured against `claude-code 2.1.235`, an interrupt keystroke ends the turn, parks the CLI back
// at its prompt, and fires **no hook at all** — not `Stop`, not a notification, ever. See
// `docs/findings/agent-interrupt.md`.
//
// So this is raised by the process source, off the keystroke ACT was asked to forward. That is a fact
// about ACT's own input channel rather than anything read off the screen, which is what keeps *ACT
// never parses terminal output* intact — and which keys mean it is the adapter's fact, since it is
// its CLI's keybinding.
public sealed record TurnInterrupted(string SessionId, DateTimeOffset At) : AgentEvent(SessionId, At);
