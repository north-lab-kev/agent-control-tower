# Rules engine

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

Drives all transitions in the machine-controlled region. Operates on
**normalized events** — each adapter maps its agent's raw hooks (Claude Code
`Stop` / `Notification` / `PreToolUse` / …; Codex's set) into ACT's shared
vocabulary, and the table is written against that. One transition is **not**
event-driven: **Ready → Executing** is ACT-initiated (it spawns the process — an
action, not a rule). **Your turn → Completed** is the human's explicit call — a
drag on the board, or the session view's Complete action. Everything else, including recovery out of Your turn, reaches ACT as an
*observation* — because the user acts in the terminal, not in ACT, so ACT learns
about it the same way it learns about anything else.

Note what the merged column does to this table: past Executing every row targets
the same column, so the **badge is the only thing the event decides**.

| Current | Normalized event | → Column | Badge |
|---|---|---|---|
| Executing | activity (tool use / turn progress) | Executing | `running` |
| Executing | compacting (`PreCompact`) | Executing | `compacting` → `running` |
| Executing | permission requested *(observed)* | Your turn | `needs permission` |
| Executing | question asked *(observed)* | Your turn | `needs answer` |
| Executing | turn ended (`Stop`) | Your turn | `to review` |
| Executing | process exited non-zero / crash | Your turn | `error` |
| Executing | the agent reports the turn failed *(api error, model refused)* | Your turn | `error` |
| Executing | startup prompt waiting *(no hook at all since the spawn)* | Your turn | `needs permission` |
| Your turn | activity observed *(the user answered, or sent it back, in the terminal)* | Executing | `running` |
| Your turn + `needs answer` | permission requested | *(ignored)* | *(stays)* `needs answer` |
| Your turn | startup prompt waiting | *(ignored)* | *(stays)* |

## There is no completion signal

A `Stop` is a `Stop`: **every turn end lands in Your turn with `to review`**, and
ACT does not try to tell a finished turn from one that ended on a question. No
self-reported status is needed:

- **`Stop` does the routing.** Measured and verified live — the card walks to Your
  turn on the hook alone.
- **The badge distinction does not matter.** Both outcomes target the same column
  (see *Why one column and not two*) and differ only in a label.
- **The two blocked cases are observed, not self-reported, and with better fidelity.**
  A permission gate arrives as `Notification`/`permission_prompt`; a question arrives
  as `PreToolUse`/`AskUserQuestion` carrying the actual question text. The only case
  left uncovered is an agent that ends a turn asking something in *prose*, and that
  costs the user one click to discover.
- **Nothing consumes a "done" assertion.** Only auto-completion would need a positive
  "done, nothing blocking" signal, and there is none (see *No auto-completion*), so
  nothing in ACT needs the agent to assert anything.

What that buys: nothing for the agent to write, no advisory rule it can silently
fail — and an injected preamble that shrinks to nothing (see *Agent ↔ ACT contract*).

## No stale badge — the quiet chip instead

**The question that decides it:** every block on the user already moves the card. A
permission prompt, a question, the pre-session trust screen — each has a signal, and
each lands in Your turn. So what is left that sits in Executing and is genuinely
hung? Not the model: measured across 2,729 within-turn gaps in real Claude Code
transcripts, p50 is 1.3 s, p99 is 45 s, and **every gap past three minutes turned out
to be a session waiting on its human**, not working. An agent that stops answering is
not a thing this codebase has evidence for.

What silence in Executing actually means, honestly enumerated:

- **A Codex card whose hooks the user declined to trust** — answering the hook-review screen
  with *"Continue without trusting"* means no payloads at all, and nothing else names the
  rollout either, so such a card really can only be read through silence.
- **Blocked *inside* a tool call** — permission was already granted and the command
  itself is waiting (an interactive CLI reading stdin, an install stuck on a private
  registry). The agent is not asking, so no hook fires.
- **A rate limit hit mid-turn** — the CLI sits showing a reset time. The
  usage-limit hold is a *Ready*-column chip for the queue runner; nothing covers this.
- **Auth expiry mid-session** — not a tool permission, so no notification.
- **ACT having lost sight of a healthy agent** — hook port moved, generated settings
  clobbered, session resumed in the desktop app. The card claiming `running` is then
  the *lie*, and silence is the only way to catch it.
- **The pty layer**, which is ACT's own code path and not one the CLI's normal users
  exercise.

Every one of those is unknowable from silence alone — and `running` is still ACT's
best knowledge. So **ACT states the gap and claims nothing**: past a threshold
(`QuietSession.Threshold`, 15 minutes — ~20× the measured p99) the strip renders a
`quiet 22m` chip **beside** the badge, which keeps saying `running`. No event, no
transition, no state.

What this costs, and it is a real cost: **no history**. A watchdog would have written
a transition, so a morning-after timeline could say "went quiet at 03:12"; the chip
only ever shows the current gap. Accepted deliberately — a recorded guess is still a
guess, and the badge it would have overwritten carried information the guess does not.

Consequences: `Badge.Stale`, `NoActivityElapsed` and `TransitionReason.NoActivity` are
deleted, the rules engine has no rule for silence, and the board carries a 30-second
refresh tick (presentation only, and only while a card is in Executing) because a
number derived from a stamp has nothing to push a re-render. If a real hang ever shows
up, this becomes a badge again — with evidence behind it.

## Other resolutions

- **`error` has two sources, and they see different failures.** ACT owns the
  spawned process, so a non-zero exit → `error` (→ Your turn) is the one failure it
  can report first-hand. The other is the agent's own report that a *turn* failed
  while its process stays alive and would exit zero — an api error, a model the
  account cannot use. Codex writes that into its rollout as `task_complete` with an
  `error`, and it becomes `TurnFailed`; without it the card would offer an api error
  up for review as if it were finished work.
- **`permissionMode` changes frequency, not the table.** `default` ⇒ "permission
  requested" fires often; `acceptEdits` ⇒ less; `auto` / `dontAsk` / `bypass` ⇒
  it effectively never fires (cards sail to `Stop`), since none of those prompt.
  A `dontAsk` denial is not a normalized event — the agent absorbs it and keeps
  going, so it surfaces only in the transcript, never as a badge. Under the PTY
  model this knob matters *more*: every prompt it produces is one the human must
  walk over to a terminal and answer.

## Error handling & retry

An `error` in Your turn is **manually retriable** — **Retry** resumes the session
(`--resume <sessionId>`) and asks it to continue → back to Executing. Three
constraints, all deliberate:

- **It sends a continue-message, not `initialPrompt`.** Re-sending the opening
  instruction to a session forty turns deep tells it to start the work over; by then
  that instruction describes a beginning that no longer exists, and the transcript the
  resume just reopened is the real context. So the message says only that the session
  was interrupted (`RetryInstruction`, localised like everything ACT writes), and it
  rides the resume command line — ACT still types nothing into a terminal.
- **It is offered only when the process is gone.** A CLI that is still alive is one the
  user can type into, and its terminal is a click away; offering Retry there would mean
  killing a usable session. So Retry is the case the user cannot handle themselves, and
  it needs no kill and no new input path. Its home is the **board strip** rather than
  the session rail, because opening a failed card's terminal already resumes it.
- **There is no fresh-seed fallback.** Duplicating the task already makes a new card
  with the same configuration, so a "start over from `initialPrompt`" path would be a
  second way to do an existing thing.

Error kinds and what retry means:

- **Transient / environmental** — *model not available*, *out of tokens /
  rate-limited* mid-run, network. Retriable once the cause clears. For these the user
  **edits `launchConfig` first** (e.g. switch `model`) then retries — which works
  because the task form freezes the prompt past the launch boundary but deliberately
  leaves the launch config editable (see *Interaction*). For a limit hit, Retry becomes
  available once the window resets (optionally auto-retry — ties to scheduling
  backpressure, but default is manual).
- **Execution** — crash / non-zero exit / tool failure. Retriable (resume), may
  warrant investigation first.
- **In-session git failure** — not a distinct case: the agent's turn ends, the card
  lands in Your turn like any other, and the user sees the failure on review. ACT does
  not detect it, because nothing reports it.

So the drawer's action set is state-specific — and, since ACT no longer answers
anything, mostly a way *into* the terminal: permission / question → **Open
terminal**; **error → Retry (± edit launch config)**; review → complete, or
Open terminal to tell it what to do next. Open terminal, Open in Desktop and
Restart terminal are available on any active card.

## No auto-completion

**Every finished turn stops in Your turn, and only the user's drag reaches
Completed.** There is no `autoComplete`, and no card ever signs itself off.

- **Completion is the one thing ACT asks of the user, and it is the whole point of
  the board.** A tower that closes its own cards is a log, not a control tower —
  the value is that a finished run *waits* for you to look at it. Making the
  sign-off skippable makes the review optional, and an optional review is the
  first thing to get skipped on the night it mattered.
- **It removes ACT's only would-be dependency on the agent's self-report.**
  Auto-completion would be the sole consumer needing a positive "I am done and
  nothing is blocking me" assertion from the agent, because `Stop` alone cannot
  distinguish finished work from a turn that ended on a question asked in prose.
  Without it, the observed events carry the whole model — see the *Agent ↔ ACT
  contract*.
- **Control arc stays human → machine → human**, with no opt-in that drops the
  final step.

**There is no git feature either, and no `autoGit`.** A task that should end in a commit,
a push or a pull request says so in its prompt — one sentence the user writes, which ACT
neither stores, phrases nor translates. It was once a dropdown on the task form whose
entire effect was to append that sentence at launch; the field, the template field, the
MCP argument and the four localised sentences all bought what a line of prompt already
said. The agent does the work in-session, the card still stops in Your turn for sign-off,
and a failed git step is simply something the user sees on review. Git decided at *review*
time needs no mechanism for the same reason: the terminal is already in front of the
reviewer.

## Agent ↔ ACT contract — one MCP server

**The agent→ACT contract is a single MCP server, `act`, and nothing else.** There is
no status file, no follow-up file, and nothing written into the task's working
directory. Everything ACT knows
about a running session it *observes* (hooks, transcript, process); what an agent may
**do** is create a follow-up card, and what it may **ask** is what is already on the
board.

| | |
|---|---|
| Transport | streamable HTTP on ACT's kept hook port, `/mcp` |
| Auth | the session's existing hook token, `x-act-hook-token` |
| Server name | `act`, so the pre-allow ids read `mcp__act__…` |
| Tools | `create_followup` (write), `list_tasks` and `get_task` (read) |

### `create_followup`

| Argument | Values | |
|---|---|---|
| `title` | | required |
| `prompt` | | required |
| `agent` | `same` \| `claude` \| `codex` | a Claude session may spawn a Codex card and back |
| `model` | `same` \| any the chosen agent supports | free string, not a static enum — the valid set depends on `agent` |
| `effort` | `same` \| any the chosen agent supports | as above |
| `permission` | `same` \| `default` \| `plan` \| `acceptEdits` \| `auto` \| `dontAsk` \| `bypass` | |
| `schedule` | `manual` \| `now` \| `next_window` | default `manual`; no `same` — see below |
| `cwd` | | defaults to the parent's working directory |
| `dependsOn` | any card ids | not restricted to this session's own children |
| `clientKey` | | optional idempotency key |

Returns the minted `id` and `number`, the **resolved** agent / model / effort /
permission, and the list of adjustments — so an agent that asked for a model the
chosen CLI does not have learns what it actually got.

**`same` means the parent's value, and it is the default everywhere it exists.** Not
the user's global defaults and not a template: a follow-up is the same work in the
same tree, so the parent is the honest baseline. Everything the agent does not supply
is inherited, and the resolution runs through `LaunchConfigResolver` like any launch —
an unsupported value is substituted with a recorded adjustment or rejected with a
message, never silently dropped.

**`schedule` is the one knob with no `same`, because the parent's is not inheritable.**
A schedule is a launch trigger that already fired; inheriting `specificDateTime` would
mean a date in the past and inheriting `now` would mean a child that launches the
instant it lands. So the values are the three that mean something for a card that does
not exist yet, and the default is `manual` — the Ready queue, gated exactly as any
other Ready card.

**`permission` can escalate, and that was accepted rather than
overlooked.** A child may be *more* permissive than its parent, up to `bypass`. It
was raised as a privilege-escalation shape and the clamp was declined, on the grounds
that the child still lands in **Ready** and the user still gates every launch — so an
escalation is visible on the board before anything runs, which is the same protection
the launch boundary already gives every other card.

**What is deliberately not an argument.** `parentId` — the token identifies the caller
(see below). `extraFlags` / `env` / `agentBinary` — arbitrary process control handed to
a model. `allowConcurrentWorkingDir` — the folder guard is the user's, not the agent's
to waive. `attachments`. And the landing column, because an agent does not choose its
own leash length.

**The folder guard makes spawning sequential, and that is the intent.** A child
inherits the parent's `cwd`, and the parent holds that folder until it is **Completed**
— Executing and Your turn both hold it. So spawned cards queue in Ready behind their
parent's sign-off rather than running beside it. Recorded here because it reads like a
stuck queue and is the guard working: two agents in one working tree is the collision
it exists to prevent.

**A cap of 100 follow-ups per card, for the card's whole lifetime.** Counted off
`children[]`, so a relaunch or a restore does not refill the budget. At the cap the
call returns an error the agent can read rather than failing silently. Configurable
under *Settings → Advanced*, because 100 is a runaway-loop backstop rather than a
considered limit.

### `list_tasks` and `get_task` — reads, board-wide

**This is the first time ACT tells an agent anything**, and the scope is the whole
board rather than the caller's own lineage.

- **`list_tasks`** — summaries of on-board cards: `id`, `number`, `title`, `column`,
  `badge`, `agent`, and whether this session spawned it. No prompts, no paths, no
  metrics. Optional column filter; archived and deleted excluded; capped at 200 with an
  **explicit truncation note** rather than a silent trim.
- **`get_task`** — one card in full: the summary plus `prompt`, `cwd`, model / effort /
  permission, `parentId`, `children`, `dependsOn`, timestamps. Never `sessionId` and
  never attachment paths.

**Why board-wide, when own-lineage would have been safer.** Two things need it.
`dependsOn` may name any card, and an agent cannot depend on what it cannot discover —
own-lineage reads would have made that argument unreachable in practice. And a long
session that has been compacted re-proposes follow-ups it already created; reading
first is the only defence it has. **The cost, accepted:** a session working in one repo
can read the titles of tasks in every other, and that text lands in its transcript and
possibly in a commit message.

**`clientKey` covers what a read cannot.** Read-then-write races; an idempotency key
does not. Two calls carrying the same key under the same parent return the same card
instead of two.

### Server `instructions` — both CLIs read it, and Claude Code may read *only* it

Confirmed from both vendors' documentation:

- **Codex** reads the `instructions` field returned at initialization and uses it as
  server-wide guidance alongside the tools, and asks that the **first 512 characters**
  be self-contained — that is what it has when deciding whether to use the server.
- **Claude Code** loads it at session start and truncates it at **2KB**. The important
  part is its interaction with tool search: with deferred tool loading, *only tool names
  and server instructions* are in context at session start. So `create_followup`'s own
  description may not be loaded when the model is deciding whether ACT can do this at
  all — the server instructions are.

That inverts the earlier assumption that instructions were optional framing. ACT writes
them, with the load-bearing content inside the first 512 characters and the whole thing
under 2KB.

### Transport — measured from both vendors' docs, and both fallbacks are dead

- **Codex** supports streamable HTTP with custom headers, including
  `env_http_headers`, which maps a header name to an **environment variable name**. So
  the token rides the process environment exactly as the hook forwarder's already does
  and the generated profile stays byte-identical across launches. The ACT-shipped stdio
  shim and the token-in-the-url fallback are both unnecessary.
- **Claude Code** takes `type: "http"` (`streamable-http` is an accepted alias) with
  static `headers`. Its `--mcp-config` file is written per launch anyway, so the token
  goes in the file.
- **`[mcp_servers.*]` is honoured inside a Codex *profile layer*** — type-probed
  (table in *Codex hook findings*), so ACT's `$CODEX_HOME/act.config.toml`
  carries the block and no `-c` fallback is needed. Adding it raises **no new trust
  gate**, and the block survives Codex's own rewrite of the file (Codex reorders it
  below `[hooks.state]`, which `WriteExternalPreservingTail` already survives).

**Why a tool and not a file or a curl command.** All three cost about the same in
tokens — 150–250 in a prompt-cached prefix — so the deciding factors were elsewhere:

- **The payload is the wrong shape for a shell command.** `prompt` is long free text
  with quotes and newlines in it; getting that through a `curl -d` body and whatever
  shell the agent's Bash tool got is the same class of bug that truncated the launch
  argument (see *Launch*). A typed schema removes the quoting question entirely.
- **The grant is narrower.** One pre-allowed tool id (`mcp__act__create_followup`)
  versus allowing the agent all shell `curl`, or all writes under a directory.
- **It sidesteps both sandboxes — verified live.** The MCP connection is
  made by the CLI's own process, not by a sandboxed tool invocation, so Codex's
  `workspace-write` network block and Claude's bash sandbox never enter the picture;
  both CLIs reached ACT's loopback endpoint from inside a live session. Claude Code's
  generated `permissions.allow` also really does pre-allow the `mcp__act__…` ids —
  three tool calls, no approval prompt.
- **Tool definitions do not decay.** A preamble rule is 80k tokens back by the end of
  a long session; a tool is re-presented every turn, at the moment the model might
  reach for it.
- **`dependsOn` gets simpler.** The file scheme needed sibling *sequence handles*
  (`["001"]`) because the agent could not know the ids ACT was about to mint. A tool
  call **returns** the created card, so the agent passes real ids and ACT resolves
  nothing.

**Correlation is the token, never an argument.** The token identifies the task, so
the parent is inferred and `parentId` is not a parameter — an agent cannot spawn a
follow-up onto somebody else's card. Same trust property the hook endpoint already
has, and the reason a shared, byte-stable config file cannot carry the token.

**There is no injected preamble.** ACT teaches the agent no conventions, so the
opening prompt is the user's task text alone — nothing rides the launch argument
but the task, and the one thing that ever did (a git sentence chosen on the form) was
removed rather than kept as an exception: the user writes it in the prompt.

**One thing is still appended, and it is a path list.** When the task carries
attachments, `AttachmentInstruction` adds a header and one absolute path per line —
the file is already on the machine, and a path is the only way an agent can reach it.
Composed at launch and never stored, so `Card.InitialPrompt` stays verbatim for the
life of the task as the form promises.

**Text ACT sends to an agent is localised, like everything else it writes.** The agent is
addressed in the language the user runs ACT in, not in English by default — so
`Act.Core/Resources/CoreStrings` exists and the core owns resources of its own.
`AppCulture` sets `DefaultThreadCurrentUICulture` process-wide, so a lookup in the core
follows the UI language from any thread, including a launch that comes from the queue
runner rather than a click.

**ACT does not announce the tool, because the tool announces itself.** The obvious
question is whether the agent needs to be *told* the MCP tool exists and when to use
it. It does not, and a preamble would be the wrong place for it anyway:

- **Existence is automatic** — an MCP tool arrives with its name, description and
  schema in the model's tool list, re-presented every turn.
- **"When and why" goes in the tool description**, which beats a preamble on every
  axis: it does not decay over a long session, it is present at the moment of the
  call rather than 80k tokens back, it costs nothing per launch, and it never touches
  the launch argument. The description carries the *when not to* as well — a
  follow-up is for work that belongs in its own task, not for deferring part of the
  current one.
- **Broader framing goes in MCP's own `instructions` field**, not in a preamble — it
  travels over the connection rather than in the opening prompt. Both CLIs read it,
  and on Claude Code with tool search enabled it is the *only* prose loaded at session
  start; see *Server `instructions`* above.
- **It keeps the read-only stance total.** ACT never parses the screen, never answers
  a prompt, and every source reports rather than commands. An injected instruction
  block is the write-side version of exactly that — dropping it makes the principle
  complete instead of nearly so.

One candidate for a future one-liner, unrelated to MCP and deliberately deferred:
since nothing auto-completes, the agent's closing message is what the user reads on
the card when deciding to sign off. Telling the agent that might improve it — but
leaving it out is the only way to learn whether it needs saying.
