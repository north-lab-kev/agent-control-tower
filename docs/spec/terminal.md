# Liveness & the embedded terminal

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

**Single mode: managed under a PTY.** ACT spawns the agent's **real interactive
TUI** in a pseudo-terminal it owns (ConPTY on Windows, a Unix pty elsewhere) and
hosts that terminal inside ACT, full-screen per card. ACT is the control tower
around the session, not a replacement front-end for it.

The division of labour this buys is the whole point:

| | |
|---|---|
| **ACT** owns | which sessions exist, where they sit on the board, what needs you, metrics, lineage, scheduling |
| **The TUI** owns | the conversation — every prompt, permission dialog, plan approval and answer |

## Alive while the card is active

The process is spawned at launch and stays **alive across turns** — through
Executing and Your turn alike — until the card completes, the CLI exits, or the
user restarts the terminal (which resumes the same session into a new pty). This is the natural shape for an interactive terminal: scrollback
survives, and the user can keep typing without ACT re-spawning anything underneath
them.

*(This reverses an earlier "live during a turn, torn down between engagements"
model, which existed to serve a headless transport that no longer applies. The
cost is real and accepted: N active cards means N live agent processes.)*

## Transport: a pseudo-terminal, raw both ways

`Porta.Pty` spawns the CLI under a pty; bytes stream to **xterm.js** in the
browser over the Blazor circuit, and keystrokes stream back. Output is decoded
incrementally (a UTF-8 sequence can straddle two reads), batched on a ~30 ms
timer, and capped — a full-screen TUI redraws far more often than a circuit wants
to be poked. Each session keeps a **bounded scrollback** so leaving the card's
terminal and coming back replays the screen instead of showing an empty one.

## ACT never parses terminal output

**Rule, not a preference.** ACT pipes the terminal's bytes and never reads them
for meaning. The prototype's `PtyStateProbe` existed to measure the alternative,
and the measurement is the reason: its regexes had to be rewritten once *within a
single CLI version bump* (the input line moved from `│ >` to `❯`), against a
screen that redraws constantly. Any state ACT inferred that way would be
version-brittle and silently wrong. **Hooks are the state channel; the terminal is
a pipe.**

The pre-session trust prompt is the one state no hook can report, and it does not
bend this rule: what ACT observes there is its own process staying silent, never a
string on the screen. See *The one prompt no hook reports*.

## The input channel is the human at the keyboard

**ACT composes nothing for a live session.** Every byte reaching a live agent is a
keystroke the user made in the terminal ACT is showing them; ACT's only writes to
the pty are the resize, the teardown, and **a dropped file's own path** — inserted
where the cursor already is, never followed by a submit key, and only in answer to a
drag or paste the user just performed. See *Attachments*: it is what a terminal
emulator does with a dragged file, not an instruction ACT decided to send.

*(There is deliberately no **send-back message** — no UI-seeded reply typed into a
session parked at its prompt. Send-back only ever means "reply to a finished card
without opening its terminal", and in an app where the terminal is a tab on the
card, that is a worse text box a click away from a better one that is already open.
It would also put ACT in the business of guessing when a TUI is ready to be typed
into. So the rule is absolute, with no "exactly one exception".)*

The **initial prompt is not typed**: it is a positional argument on the launch
command line for both agents (and a resume message likewise). Typing it looked
equivalent and is not — measured: a CLI that has painted its banner is
not yet listening to its prompt line, so the prompt landed nowhere and the card sat
at an empty composer while the board said it was executing. On the command line it
is in place before the TUI paints, and there is no race to lose.

Everything else — answering a permission prompt, answering a question, approving a
plan, replying to finished work, `/`-commands — the user types themselves, in the
terminal ACT is showing them.

Consequently ACT has **no approve/deny surface at all**. See *Hooks are
observability, not control*.

## Restart terminal

The session view's one process action is **Restart terminal**: end the pty and
resume the *same* `sessionId` into a fresh one. The card keeps its column, its
badge and its binding, because resuming a session is not a claim about the work —
the same rule the startup restore follows. Recorded as `TerminalRestarted` rather
than `SessionRestored`, since here ACT ended a live process on purpose and the
timeline should say the scrollback was **discarded**, not lost.

It exists for the terminal that has become unusable while the session behind it is
fine — a wedged or garbled screen, a CLI that stopped painting. `RestoreAsync`
could not serve: it bails on a live session, and a live-but-useless one is exactly
the case.

**There is deliberately no kill button.** Every job one would do has a better
owner: `Esc` in the terminal interrupts a response *or a tool call* mid-turn
(Claude Code, verified in the CLI docs), so redirecting a misbehaving agent is the
terminal's job — the turn then ends and `Stop` moves the card by itself; a
genuinely wedged card is **deleted** — soft, restorable from the archive — which is
the honest verb for abandoning work; and a broken screen is what Restart terminal
fixes, without costing the run. Two invariants follow:

- **Disposal is the only teardown a session has** — and it is what retires the
  session's hook token and config file, so no path can end a process without
  `SessionRegistry` cleaning up after it.
- **`Badge.Killed` is kept, and is unreachable.** It stays in the model, the
  attention order and the colours so that a card stored with it still reads back,
  and so signing off a card carrying it keeps working.

## Handoff to the Claude desktop app — a per-card action

`claude://resume?session=<uuid>` **works**: read out of the desktop app's own
`app.asar` (v1.24012.9.0) and confirmed by the prototype, it validates a canonical
UUID and calls `importCliSession`, which reads the CLI transcript from disk. So a
card ACT launched can be opened in the desktop app whenever the user prefers that
surface.

- Offered as an **"Open in Desktop" action on a card**, never as a launch mode.
  ACT always launches under the PTY, so it always owns the session id, the hooks
  and the metrics; the desktop app is an additional window onto the same
  transcript, not an alternative to being observed.
- Note the parameter is `session`, not `sessionId`, and the route reads no `cwd` —
  it locates the transcript itself. Named failure modes: `transcript_missing`,
  `auth_expired`, `network`.
- `claude://code/<id>` is a real route but feature-gated and keyed by a *bridge*
  id, not a CLI session id — not usable here.
- The knowledge is **adapter-local** (`IAgentAdapter.DesktopHandoffUrl`), so agents
  with no desktop app simply return null and the action does not appear.

## Rejected alternatives

Three other liveness routes were built or probed and rejected — the stream-json
control protocol, desktop-only handoff as the launch mode, and desktop GUI
automation. The reasoning is in `design-notes.md` → *Rejected liveness routes*;
stream-json stays on the table only as a possible future unattended mode (see
*Future enhancements*).
