# Ingestion & session identity

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

## Pluggable, multi-source ingestion

Ingestion is **transport-agnostic** and, since the PTY decision, **entirely
observational** — every source reports, none of them commands. ACT defines source
types; each **agent adapter composes whatever mix of sources fits its agent** —
including several at once for a single agent. All sources normalize into **one
event stream** that the rules engine consumes. The rules engine never learns *how*
an event arrived, only its normalized type — so adding a transport is adding a
source, with no downstream change.

Flow: `sources (per adapter) → normalize → event bus → rules engine + store`.

**A source is a publisher, not a port.** Each one normalizes on its own side and
**pushes** into `IAgentEventSink`, which the session registry resolves to the live
session; the session hands the stream on to the rules engine. There is deliberately
no `IIngestionSource` interface to implement, because nothing about a hook post, a
file change or a dying process is something ACT pulls from.

**Source types:**

1. **HTTP hook** — agent POSTs event JSON straight to ACT's local endpoint
   (Claude Code `http` hooks). No shell/`curl`; cross-platform clean.
2. **Command-hook forwarder** — a command hook pipes stdin → ACT's endpoint
   (Codex, which supports command hooks only; also usable by Claude Code). A
   tiny shipped helper or `curl`.
3. **Transcript source** — tails the session's JSONL transcript for **enrichment
   only** (observed model, token counts, context %, last message) and, for an agent
   that cannot pre-mint its id, for the **session binding** and turn boundaries.
   Read-only; ACT never writes into a transcript, and the only file it watches is
   the agent's own.
   - **How it reads:** polled once a second from a stored offset, not watched — an
     appended file needs a debounce to watch and reports late anyway, while a read
     from an offset is a few kilobytes. The **first** read takes the whole file, so a
     restart or a resume does not report an hour-old session as if it had just
     started; the offset comes back from the reader rather than being taken from the
     file's length, because a line the agent is mid-way through writing has to be
     left for the next read. A file that has *shrunk* was replaced, so the read
     starts over.
   - **Where the path comes from:** whichever hook payload arrives first names it
     (`HookNormalization.TranscriptPath` → `IAgentEventSink.LocateTranscript`), which
     is deliberate rather than convenient — `SessionStart`, the payload designed to
     carry it, was never observed firing. Deriving the path instead is possible for
     Claude Code (`~/.claude/projects/<cwd-slug>/<sessionId>.jsonl`) and was rejected:
     it would hardcode undocumented slug rules that break silently.
   - **What owns which number:** the hooks count turns and tool calls first-hand, so
     the transcript leaves both null and the two never fight over a field — true of
     both agents.
4. **Process source** — ACT's own process supervision of the PTY (always
   ACT-internal, not agent-provided): non-zero exit → `error`; **no hook at all
   within the startup grace → the pre-session prompt** (see *The one prompt no
   hook reports*). No idle watchdog — see *No stale badge*.
5. **MCP tool call** — the one *inbound* channel the agent drives deliberately
   rather than emits as a side effect: `create_followup`. Distinct from the four
   above because it is a request with a return value, not an observation.

**Example compositions** (adapter's choice, freely mixable):
- *Claude Code:* HTTP hooks (lifecycle) + transcript source (enrichment) + process
  source.
- *Codex:* command hooks via a forwarder script (lifecycle, activity, and
  `PermissionRequest` — the waiting-prompt event Claude Code has to infer) + transcript
  source (enrichment, plus `TurnFailed`) + process source. So the two compositions differ
  only in hook *transport*: http for Claude Code, a forwarder for Codex, which supports
  command hooks only.

Note what is **not** a source: the terminal itself. ACT pipes the PTY's bytes to
xterm.js and never reads them for meaning (see *ACT never parses terminal output*).

## Hooks are observability, not control

**ACT observes prompts; it never answers them.** When the agent asks for
permission or asks a question, that arrives as a fire-and-forget hook
(`Notification`, and `PreToolUse` for the tool about to run). ACT normalizes it,
moves the card to Your turn with the right badge, and shows a **read-only**
summary of what is being asked. Answering happens where the prompt actually lives:
in the TUI, in the card's terminal, typed by the user.

This is what makes the "hooks must never slow the agent" rule strictly true rather
than aspirational — ACT has no decision to make, so it can accept-and-return
immediately in every case. It also means no ACT bug can approve something on the
user's behalf.

### Classifying a waiting prompt (measured against `claude-code v2.1.220`)

`Notification` fires for more than one thing, and its *message* cannot separate the
three cases ACT cares about. Two payload fields can, and both are structured rather
than prose:

| What is happening | Hook | The field that says so |
|---|---|---|
| Waiting on a permission prompt | `Notification` | `notification_type: "permission_prompt"` |
| Idle nudge, ~1 min after a turn ends | `Notification` | `notification_type: "idle_prompt"` |
| Waiting on a **question** to the user | `PreToolUse` | `tool_name: "AskUserQuestion"` |

- **`notification_type`, not the wording.** Both notifications carry a fixed English
  sentence (*"Claude needs your permission"*, *"Claude is waiting for your input"*), and
  the wording is not even unique to a permission prompt — see the next point. ACT keys on
  the type and keeps the substring check only as a fallback for a payload that omits it.
- **A question is announced as a permission prompt.** The CLI blocks on `AskUserQuestion`
  behind an ordinary permission prompt, so its `Notification` is byte-for-byte the one a
  `Bash` approval fires. The **tool name** on the preceding `PreToolUse` is the only thing
  that tells the two apart, and its `tool_input.questions[]` is the only place the question
  text exists at all — the notification has none. So `PreToolUse` for that one tool
  normalizes to a **question**, not to activity.
- **The notification that follows the question is dropped by the rules engine**, not by the
  normalizer: it arrives second and describes the same block, and unsuppressed it would
  replace `needs answer` with `needs permission` and nothing to approve. The rule is
  stateless and agent-agnostic — *a permission request never overwrites an unanswered
  question* — and nothing legitimate is lost, because the agent is stopped until the user
  answers in the terminal and that answer arrives as activity.
- **An unrecognised notification produces no event at all.** Reporting it as a permission
  request badges an idle card `needs permission` with nothing to approve, and reporting it
  as activity is worse, since an idle nudge means the opposite of activity and would send a
  reviewed card back to Executing on its own.

### The one prompt no hook reports (measured against `claude-code v2.1.220`)

Both CLIs open on a **directory-trust prompt** for a working directory they have not
seen before — *"Is this a project you created or one you trust?"* — and it blocks
**before the session exists**. Measured by launching under `PtyHost` against a fresh
directory and leaving the prompt up: **not one hook fires, for as long as it is left
there — not even `SessionStart`.** So there is no payload to normalize, and the card
sat in Executing badged `running` while the CLI waited on a keypress.

The signal is therefore the **absence** of one, and it belongs to the process source
because it is a fact about ACT's own child process rather than anything read off the
screen:

- **No hook of any kind within the startup grace → `startup prompt waiting`.** The
  grace is **8 s**, an order of magnitude over the ~0.8 s a directory the CLI already
  trusts takes to produce its first hook.
- **Terminal output does not disarm it.** A CLI parked on its trust prompt paints a
  full screen and has still started nothing. Only a hook proves a session exists.
- **Executing only.** A restore deliberately leaves a card where the rules last had
  it, and a card already in Your turn is blocked on something the agent named, which
  says more than silence does.
- **Recovery needs no rule of its own.** Answering starts the session and its first
  hook arrives as activity — measured at ~370 ms after the keypress.
- **Overshooting is cheap.** A slow start costs the card two extra transitions and
  nothing else, because that first hook puts it straight back.

## Session identity / correlation

Every hook payload includes `session_id`, `transcript_path`, `cwd`, and
`hook_event_name` (per-turn events also add `permission_mode`, `effort`). So
correlation is deterministic:

- `session_id` → look up the card by the `sessionId` ACT **pre-minted** at
  launch. Exact.
- **Unknown `session_id`** (a session ACT didn't launch) → **ignore** (ACT is
  orchestrator-only). The one exception is the **first** hook of a reported-id
  agent: ACT matches it by the task id on the process environment and *learns* the
  session id from it, then correlates by `session_id` forever after.
- `transcript_path` → the exact JSONL to tail for enrichment (no path
  reconstruction).

## Reliability

- Hooks are **synchronous and block the agent** until they return (with
  timeout). ACT's endpoint must **accept-and-return instantly**, processing
  asynchronously, so it never slows the agent. (Claude Code also allows a hook
  to background itself via `{"async":true}`.)
- **Per-card event serialization** — process one card's events in order; handle
  concurrent POSTs and LiteDB write concurrency.

## Hook injection (no clobbering)

ACT registers its hooks via a **dedicated settings file passed at launch**,
never by editing the user's committed `.claude/settings.json` — keeping ACT's
instrumentation isolated from the user's own hooks. *(Exact flag/mechanism to
confirm at build.)*

## Registered events → normalized events

- `SessionStart` → capture `transcript_path`, confirm binding
- `UserPromptSubmit` / `PreToolUse` / `PostToolUse` → activity (`running`).
  `UserPromptSubmit` is also the **recovery** signal: it is how ACT learns the user
  answered a prompt in the terminal, since no answer passes through ACT.
- **question asked** → `PreToolUse` with `tool_name: "AskUserQuestion"` (Claude Code),
  read-only, carrying the question text from `tool_input`. The one `PreToolUse` that is
  *not* activity — see *Classifying a waiting prompt*.
- **permission requested** → `Notification` with
  `notification_type: "permission_prompt"` (Claude Code) / `PermissionRequest` (Codex),
  read-only
- `PreCompact` → compacting
- `Stop` → turn ended → Your turn, `to review`
- `SessionEnd` → session ended (persistence)
- process exit code (process source, not a hook) → `error`

## Edge cases

- **ACT-restart recovery** — binding persists in LiteDB (`sessionId` on the
  card), so re-correlation after ACT restarts is trivial; `transcript_path`
  persists too.
- **Resume/fork** — a known `--session-id` / `--resume` keeps the binding; a
  Codex fork producing a *new* id is the deferred `sessions[]` case.
- **ACT restart kills live terminals, and ACT brings them back.** The PTY is a
  child of ACT's process, so restarting ACT ends every session it was hosting. The
  binding survives (it is in LiteDB), so **startup resumes every card in the machine
  region — Executing and Your turn — that still carries a `sessionId`**,
  with `--resume` into a fresh terminal and no message, which drops the user at the
  prompt. The same restore happens when a card's terminal is *opened* and its process
  is gone (exited on its own, signed off, or a restart that could not reach
  it), so the terminal is never an empty pane behind a button. The on-screen scrollback
  does not come back; the restore is recorded as a transition so the timeline says why.
  - A restore **moves nothing**: the card stays in the column and badge the rules
    last gave it, because resuming a session is not a claim that work is running.
  - **Completed restores on open, never unattended.** The session is the record of
    what was done, so a signed-off card opens on its CLI history rather than a blank
    pane — but it waits to be opened, because a pty per completed task at every start
    would be a fleet of processes for work that is over. It stays Completed: the rules
    do not govern that column, so nothing the resumed session says can move it, and
    reopening it into **Your turn** remains a separate, explicit action.
  - Not restored: Ready and Preparing (nothing has been launched — that is the
    user's call). A card with no `sessionId` has no binding to resume and is left
    alone.
  - **Restart terminal is the one teardown that resumes immediately**, because ending
    the pty is the whole point of it rather than a side effect — see *Restart terminal*.
    Every other way a session ends waits for the next open or the next start.
