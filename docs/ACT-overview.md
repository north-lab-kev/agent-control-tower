# ACT — Agent Control Tower

A column-based (Kanban) control surface that shows all your coding-agent
sessions (Claude Code first, Codex later) and their states at a glance, so you
can spot which ones need your attention.

The name doubles as the verb *to act*: the tool's whole job is helping you
decide which session needs attention — and act on it.

**License:** **Functional Source License (FSL)** — source-available, not OSI
"open source". Anyone may read, use, modify, and redistribute the code; only
**competing commercial use** (reselling/rehosting ACT as a competing product or
service) is prohibited. Each release **auto-converts to Apache 2.0 (or MIT)
after two years**. This lets enterprises use ACT freely (including internally)
while preventing others from reselling it. Chosen over BSL (4-yr window,
per-project variability) and Elastic License 2.0 (never converts, hosting-focused
restriction). Ship a third-party NOTICES file for the permissive dependencies
(see Engineering standards → Dependency licensing). *Not legal advice — read the
FSL text at fsl.software and review before release.*

---

## Tech stack

Constraints driving these choices: runs **locally**, cross-OS (**Windows +
Linux** at least), and **very easy to build** (no installer planned yet —
`dotnet run` during development).

| Layer | Choice | Notes |
|---|---|---|
| Runtime / UI | **.NET 10 (LTS)** + **Blazor Server** | LTS through Nov 2028. Blazor Server's usual weakness (SignalR circuit latency) disappears on localhost, and it gives live push to the board for free. |
| UI components | **Radzen.Blazor** (free NuGet package) | 145+ native C# components (DataGrid, etc.), MIT-licensed, free for commercial use, supports .NET 10 + Blazor Server. Only the free component library — *not* the paid Radzen Blazor Studio. Requires `InteractiveServer` render mode (already in use). |
| Desktop shell | **Electron.NET** (wrapping the same app) | For a native desktop window that feels more finished than a browser tab. .NET 10 is an explicit target; Linux supported (glibc 2.31+). |
| Data store | **LiteDB** | Embedded, single-file, C#-native document database (Mongo-like API). Zero install/no server — keeps the no-installer promise. |
| Ingestion | **Pluggable multi-source** — HTTP/command hooks, `FileSystemWatcher`, process signals, + the stream-json control channel | Adapters compose a mix per agent; all normalize into one event stream. See Ingestion & session identity. |
| Dev loop | `dotnet run` (browser) → Electron.NET build for the desktop artifact | Same app both ways. |

### Guiding architectural principle

Build ACT as a **clean, standalone ASP.NET Core + Blazor Server app**, and let
Electron be nothing but a **thin window** around it. Keeping the app
Electron-agnostic means: `dotnet run` stays the fast dev loop, the desktop
build is just a packaging step, and the shell can be swapped later without
touching ACT itself.

### Build-environment caveats

- **Node.js 22.x** is required on the *build* machine (not for end users).
- The no-CLI, MSBuild-integrated Electron.NET experience ("ElectronNET.Core")
  is still **pre-release**; the stable classic path works but uses the older
  CLI flow.
- Building Linux packages from Windows needs **WSL2**.

### Cross-agent design (context)

Claude Code first, Codex later, via a **per-agent adapter** feeding one
**normalized event model**. Both agents expose near-identical lifecycle hooks
and write JSONL session transcripts, so adding a third agent later is just
another adapter.

---

## Engineering standards

Standards to build against — not optional polish. Specifics named for the
.NET/Blazor stack.

### Architecture

- **Ports-and-adapters (hexagonal).** A **domain core** (cards, state model,
  transitions, rules engine, scheduling) that is independent of both the UI
  (Blazor) and the agents (CLIs). The core speaks only in **normalized events**
  and interfaces; it must not reference Blazor, a specific CLI, or the file
  system directly.
- **Ports as interfaces.** The agent adapter and each ingestion source are ports
  behind interfaces; concrete adapters (Claude Code, Codex, …) and the store
  (LiteDB) are plug-ins. Adding an agent must not touch core logic.
- **Dependency injection** via the built-in .NET DI container; nothing news-up
  its own dependencies across a layer boundary.
- **Nullable reference types on**, warnings-as-errors on the core projects,
  .NET analyzers enabled; `dotnet format` enforced.

### Testing (fast, deterministic, no real CLIs)

- **Framework:** **xUnit**, with **AwesomeAssertions** (the free Apache-2.0
  community fork of FluentAssertions — FA v8+ went commercial in Jan 2025) or
  **Shouldly** for readable assertions, and **NSubstitute** for mocking
  dependencies at the ports. *(Do not use FluentAssertions v8+ — paid license.)*
- **Unit tests — the rules engine hard.** It's pure logic (event in →
  column/badge out); test it exhaustively with no processes, files, or agents.
  Highest-value surface.
- **Mock adapter as the lifecycle fixture** (roadmap step 6). Drive whole task
  lifecycles — scheduling, spawning, auto-complete — through scripted fake
  events, deterministically, no real CLI or tokens.
- **Contract tests.** One shared xUnit suite that **every** agent adapter must
  pass, so Claude Code and Codex are held to the same normalized behavior.
- **UI tests — Playwright for .NET** against the **browser-mode** Blazor app
  (`dotnet run`), driven by the **mock adapter** so flows are deterministic. Run
  against browser mode, **not** the Electron shell (single-instance-lock / CDP
  friction). Cover: task creation, drag + drag-time graying, event-driven column
  moves, the per-state drawer actions, and the "N need you" jump.
- **Coverage:** **coverlet**; weighted — domain core / rules engine ~90%+; no
  blanket 100% mandate elsewhere.
- **No automated integration tests against real CLIs** (deliberate — slow,
  flaky, token-costly). Real-CLI behavior is verified **manually** via the
  build-time spikes and each roadmap step's *verify* line.

### CI (required)

- **GitHub Actions**, running the **full fast suite** (unit + contract +
  Playwright UI) on every push / PR. Blocking.
- Matrix on **Windows + Linux** (matches the cross-OS requirement); include the
  Playwright browser-install step.
- The release-build script (roadmap step 2) is the CI build hook.

### Observability & operational

- **Structured logging** via **Serilog**, with a **per-session correlation id**
  (the `sessionId`) so a task's whole trace is filterable — near-essential when
  orchestrating opaque subprocesses.
- **No silent failures** — matches the spec's ethos (explicit fallbacks: the
  To-review status fallback, error→retry, resume→fresh-seed). Surface errors to
  the card/badge, never swallow.

### Local-endpoint security

- The hook/ingestion HTTP endpoint binds to **localhost only**, on a
  **random port**, and requires a **per-session token** (injected into the hook
  config at launch) so only ACT's own launched agents can post to it.

### Dependency licensing

- All shipped dependencies are **permissive** (MIT / Apache 2.0 / BSD) — .NET,
  Blazor, Radzen.Blazor, Electron.NET, LiteDB, Serilog, etc. — so they combine
  freely into a work distributed under ACT's chosen source-available license.
  **No GPL/AGPL dependencies** (would conflict with a non-open license).
- Obligation: ship a **third-party NOTICES file** preserving the dependencies'
  copyright/license notices (attribution is the main permissive-license duty).
- Keep test-only tooling free: **avoid FluentAssertions v8+** (commercial); use
  AwesomeAssertions / Shouldly. Re-check any new dependency's license before
  adding it.

### Code quality

Clean-code principles, stated as pragmatic guidance (not scripture — a few
"Clean Code" tenets are contested; favor judgment over dogma):

- **Readable and self-documenting** — clear names, small focused functions and
  classes, single responsibility. Use the spec's **domain vocabulary** (cards,
  columns, badges, transitions, adapters, sources) verbatim in the code so spec
  and code stay legible together.
- **SOLID**, especially at the seams we rely on — single-responsibility and
  dependency-inversion at the **port boundaries** (they're what make the
  adapters swappable and the tests deterministic).
- **Comment the *why*, not the *what*** — and explicitly flag the deliberately
  odd, load-bearing bits (the underdocumented stream-json control protocol,
  atomic temp→rename file writes, the launch-boundary rule) so a future reader
  doesn't "tidy away" a quirk that matters.
- **Consistent style, enforced by tooling** — `dotnet format` + analyzers do the
  mechanical enforcement, so consistency isn't left to willpower.
- **DRY with judgment** — don't over-abstract. The two adapters will look
  similar without being the same; premature deduplication there fights the
  abstraction.

---

## State model

A card carries two independent things: a **column** (where it sits in the
workflow) and a **badge** (the live health/reason of its session). Columns can
be human- or machine-controlled; **badges are always automatic.**

### Summary

| # | State | Control | Enters by | Exits to |
|---|---|---|---|---|
| 1 | Preparing | Human | user creates task (initial) | Ready |
| 2 | Ready | Human | manual from Preparing, **or spawned** from a completing task | Executing (launch) · Preparing (back) |
| 3 | Executing | Machine | auto, on launch | Needs feedback · To review |
| 4 | Needs feedback | Machine | auto: permission / question / error | Executing (after input) |
| 5 | To review | Machine | auto: `Stop`, clean | Completed · Executing (send-back) |
| 6 | Completed | Human *(or auto)* | manual from To review; **or auto** on clean finish if `autoComplete` | To review (reopen) |

**Control arc:** human → machine → human. Control starts with the user
(Preparing, Ready), passes to the agent at launch (Executing, Needs feedback,
To review), and returns to the user at the end (Completed). ("Control" here
means who drives movement *out of* a state. A spawned task is *created* into
Ready by machine/agent, but the user still controls its exit — gate the launch
or send it back to Preparing — so Ready stays human-controlled.)

**Launch boundary (key rule):** the moment a card enters Executing, the user
can no longer move it by hand. Columns then change *only* through automated
transitions — driven by **hooks** (in-session events) and by **process signals**
(exit code / no-activity timeout, which hooks can't report, e.g. a crash). The
one exception is the manual **Completed → To review** reopen — which re-enters
the workflow rather than overriding a live session, so the rule still holds: no
manual moves while a session is actively mid-flight.

**Badges (orthogonal to columns, always auto):** `running`, `needs permission`,
`needs answer`, `error`, `killed` (user-terminated via the kill/abandon hatch),
`stale` (no activity past a timeout — possibly hung), `compacting` (context
being compacted), `idle` (awaiting review). One event can both move a card *and*
stamp its badge. Note: `stale` and `compacting` occur *during* Executing;
`stale` does **not** auto-escalate (see Rules engine) — it stays in Executing as
a warning, and escalation is the user's call via kill/abandon.

### The states

**1. Preparing** *(initial, human-controlled)*
Every task is born here. The user is drafting the prompt / defining the task;
no agent session exists yet. Exit is always manual, to Ready.

**2. Ready** *(queue, human-controlled)*
The prompt is finalized and the task is queued but not yet launched — no session
exists yet. Reached either manually from Preparing, or by being **spawned** from
another task (see Task spawning & lineage). Exits: launch → Executing, or
manually back to Preparing if the prompt needs work.

**3. Executing** *(machine-controlled)*
First automated transition. On launch, ACT generates the session UUID, spawns
the agent CLI (e.g. `claude`) in the task's working directory with that
`--session-id` and the prepared prompt, and binds `session_id → card`. The task
is actively being worked (`running`). Auto-exits: to Needs feedback (blocked)
or To review (`Stop`, clean).

> **Liveness model** (see Liveness & input channel). Managed mode: ACT
> keeps the agent process **alive during a turn** (interacting over the
> stream-json control protocol) and **tears it down between engagements**,
> resuming via `--resume`. So "working" = a live turn; "idle" (To review) = no
> process, state on disk; "send input" = a control-response/message over the
> live stream, or a fresh `--resume` after teardown.

**4. Needs feedback** *(machine-controlled)*
The session is blocked and needs the user; the badge says why:
`PermissionRequest` → **needs permission**; a question → **needs answer**;
crash / non-zero exit → **error**. Failures route here (not To review). Exits:
for permission/question, the user provides input/approval → back to Executing
(badge returns to `running`); for **error**, the user can **Retry** (see Error
handling & retry) → back to Executing.

**5. To review** *(machine-controlled → hands back to human)*
The agent finished its turn cleanly (`Stop`, no question, no error); badge goes
`idle`. Work is produced and nothing is blocking; it is the user's turn to
inspect it. Exits: **Completed** (approve/finish) or **Executing** (send-back —
user feedback re-enters the same session).

**6. Completed** *(terminal-ish, human-controlled)*
The user marks the task done from To review. Not strictly terminal: it has one
manual exit, **reopen → To review**, for when the user forgot some feedback.
From there the normal To review exits apply.

---

## Task spawning & lineage

Tasks can be created two ways: **manually** (born in Preparing) or **spawned**
by another task (born in Ready, prompt already defined). This is a general
mechanism, not a git-specific one — **any task can spawn follow-up tasks**
(emergent; there is no special "plan" task type). A plan task decomposing into
implementation tasks and a completed task emitting a git task are the *same*
mechanism.

### Lineage

Every card carries:

- `origin` — `manual` or `spawned` (with `parentId` when spawned)
- `children[]` — the tasks this task spawned

So navigation works both directions: a parent lists its children; a child shows
where it came from. Spawn **author** is also recorded, since it affects labeling:

- **ACT-emitted** — deterministic; ACT writes the child's prompt itself (e.g.
  the git commit/push/PR task from the user's choice).
- **Agent-emitted** — the agent produces the follow-ups (e.g. plan → tasks).

### Spawned tasks land in Ready

Their prompt is already defined, so they skip the human drafting step — but they
enter the **Ready queue**, so the user still gates the launch and may send one
back to Preparing to tweak its prompt first. The launch-boundary rule is intact:
a completing task **creates** cards in Ready, it never **moves** an existing
card (creation ≠ a manual column move).

### Spawn mechanism

**ACT-emitted spawns** (e.g. the git task) skip all of the below — ACT already
has the prompt, so it creates the Ready card **directly in its own store**. No
disk round-trip.

**Agent-emitted spawns** use the file mechanism below, which reuses the existing
`FileSystemWatcher` ingestion — no MCP server or live endpoint required.

- **Location:** a spawn directory, `.act/followups/`, in the task's working dir.
- **One file per follow-up** — avoids concurrent-write / partial-read races.
- **Filename = ACT-owned prefix + agent sequence:** `<taskId>-001.json`,
  `-002.json`, … ACT reuses the **spawning task's own `id`** as the prefix
  (globally collision-free; the `.act/` dir may be shared across tasks in one
  repo); the agent fills the sequence (handles any count, no coordination). The
  filename is only a **write-safety transport handle** — ACT mints each child's
  real task id (its own UUID) on ingest, so ACT owns task-id assignment.
- **Atomic write:** agent writes to a temp name then renames into place, so the
  watcher never fires on a half-written file.
- **Read trigger:** on the `Stop` hook, ACT scans the spawn dir and mints one
  Ready card per unconsumed file.
- **Consume-on-ingest:** ACT moves each ingested file to
  `.act/followups/consumed/` — race-free and idempotent, no hashing needed.
- **Per-file schema:** `{ title, prompt, cwd?, dependsOn? }`. `cwd` is optional
  and defaults to the parent task's working directory. ACT injects both the
  spawn-dir path and this schema into the launch prompt so the agent knows where
  and how to write.

`dependsOn` is the seed of task ordering (a plan implies sequence); ordering is
enforced by the scheduling runner — a task launches only after its `dependsOn`
prerequisites are Completed.

---

## Task / card data model

The card is the central entity (stored in LiteDB). Fields, grouped by concern:

### Identity — three distinct IDs

- `id` — ACT-minted **UUID**, permanent, internal. Used for store keys,
  `parentId` lineage, and the `.act/` filename prefixes (follow-ups + status).
  Never shown to the user.
- `number` — friendly sequential **`#1039`**, display only.
- `sessionId` — the **agent's** session id (the value ACT pre-mints and passes
  via `--session-id`). **Single value; null until launch.** This is the join key
  for inbound hooks and JSONL transcripts.
  - *Deferred:* a task could span multiple session ids (Codex fork, resume /
    restart). A `sessions[]` history is intentionally **not** modeled now; adding
    it later is non-breaking. Whether it's ever needed comes down to Codex
    fork/restart behavior — the managed model resumes on the same
    `sessionId`.
- `title` — the task's name.
- `initialPrompt` — the **immutable** opening instruction, set in Preparing.
  Preserved verbatim across all later turns (enables re-run-from-scratch and
  auditing what was originally asked). Subsequent inputs — answers, permission
  grants, send-back feedback — are part of the interaction history, **not**
  overwrites of this field.

### State

- `column` — `Preparing | Ready | Executing | NeedsFeedback | ToReview |
  Completed`.
- `badge` (execution status) — `running | needs-permission | needs-answer |
  error | stale | compacting | idle`; null before launch.

### Agent / session

- `agentType` — `claude-code | codex`.
- `sessionId` — see Identity.
- `workingDir` — the task's cwd.
- `launchConfig` — per-task launch parameters (see Launch config below).
- `schedule` — *when* the task may auto-launch: `manual | now | next-window |
  window-after-next | datetime`. Set at Preparing → Ready, editable in Ready
  (see Scheduling & queue policy).
- `autoComplete` — bool, set at creation. On a **clean** finish
  (`ready_for_review`), skip To review and auto-advance to Completed (see
  Auto-complete below). Does **not** fire on error / `needs_input` / permission
  stop — those route to Needs feedback as normal.
- `autoGit` — optional, only with `autoComplete`: the git actions
  (`commit` / `push` / `pr` + `draft`) to perform **in-session** before
  completing. Injected into the launch prompt (known at creation), so it needs
  no follow-up task and no input channel.

### Lineage

- `origin` — `manual | spawned`.
- `parentId` — nullable (set on spawned tasks).
- `children[]` — **stored (both directions)** alongside each child's `parentId`.
  Chosen over a derived list to preserve **explicit child order** (meaningful for
  a plan that spawns a sequence; ties into `dependsOn`).
  - *Consistency rule:* create / delete / re-parent must update **both sides in
    one operation**, via a single centralized link/unlink routine — never ad-hoc
    — so the two copies can't drift.
- `spawnAuthor` — `act | agent` (when spawned; affects labeling).
- `dependsOn` — ordering seed on spawned tasks (enforced by the scheduling
  runner: a task launches only after its `dependsOn` are Completed).

### Timing & history

- `createdAt`, `launchedAt`, `completedAt`.
- `transitions[]` — a per-task **log**; each entry records `at` (timestamp) and
  what changed (`column` and/or `badge`, optional `note`). Powers time-in-state,
  audit trail, and a per-card timeline in the UI.

### Enrichment & metrics (read-only, observed — kept fresh from the JSONL/file source)

- `lastMessage` — the agent's most recent message (for the card preview).
- `observedModel` — the model the session **actually** ran (may differ from the
  requested `launchConfig.model` due to fallback or a mid-session `/model`
  switch). Stored alongside the requested one to avoid confusion.
- `metrics` — grouped health/at-a-glance object (derived/observed, not
  user-set):
  - `tokensIn` / `tokensOut` — cumulative input / output tokens over the whole
    task (priced differently); `tokensTotal` derived.
  - `compactions` — counter, incremented on each `PreCompact` (thrash signal).
  - `contextUsed` / `contextLimit` — **live** active-context usage vs. the
    model's window (e.g. 142k / 200k); drops after a compaction. Distinct from
    cumulative tokens. Percentage derived.
  - `cost` — derived from tokens × model rates.
  - `turnCount` — number of exchanges.
  - `toolCalls` — number of tool invocations (activity/complexity signal).
  - `lastActivityAt` — timestamp of the last event; what the `stale` watchdog
    compares against.
  - `activeTime` — wall-clock time actually executing (optional; pairs with
    elapsed-since-`launchedAt`).

### Launch config

Set at Preparing time. ACT holds a **normalized** field set; **each agent
adapter maps it** to that agent's real CLI flags / settings, defines valid
values, and defines behavior for unsupported values (map to nearest equivalent
*or* reject at launch with a clear message — never silently drop).

- `workingDir` — the task's cwd.
- `agentBinary` — path to the executable (global default per `agentType`).
- `model` — per-task; **agent-specific** values (e.g. current Claude Code:
  Sonnet 5 / Opus 4.8 / Fable 5).
- `effort` — per-task reasoning effort; **agent + model-specific** (current
  models use adaptive effort like `xhigh`; some ignore it / can't disable
  thinking — so it may be a no-op).
- `permissionMode` — **first-class normalized field**, fixed set ACT understands:
  `default | acceptEdits | plan | bypass`. Adapter-mapped (Claude Code:
  `--permission-mode …`).
  - **This is a state-machine knob, not just config.** It governs the Needs
    feedback state: `bypass` ⇒ few/no `PermissionRequest`s (runs autonomously to
    `Stop`, rarely enters Needs feedback for permission); `default` ⇒ enters it
    often. `plan` (read-only, no mutations) is the natural fit for a **planning
    task** that emits follow-ups without touching anything — pairs directly with
    the spawn/plan-decomposition pattern.
- `allowedTools` / `disallowedTools` — optional tool allow/deny.
- `extraFlags` / `env` — escape hatch for anything not modeled.

*Precedence caveat:* ACT's per-task flags layer **on top of** the project's and
user's existing `settings.json` (CLI flags > project > user > built-in
defaults). A project deny list still applies underneath — ACT does **not** fully
own permissions.

---

## Ingestion & session identity

### Pluggable, multi-source ingestion

Ingestion is **transport-agnostic**. ACT defines source types; each **agent
adapter composes whatever mix of sources fits its agent** — including several at
once for a single agent. All sources normalize into **one event stream** that
the rules engine consumes. The rules engine never learns *how* an event arrived,
only its normalized type — so adding a transport is adding a source, with no
downstream change.

Flow: `sources (per adapter) → normalize → event bus → rules engine + store`.

**Source types:**

1. **HTTP hook** — agent POSTs event JSON straight to ACT's local endpoint
   (Claude Code `http` hooks). No shell/`curl`; cross-platform clean.
2. **Command-hook forwarder** — a command hook pipes stdin → ACT's endpoint
   (Codex, which supports command hooks only; also usable by Claude Code). A
   tiny shipped helper or `curl`.
3. **File source** — `FileSystemWatcher` on known paths: `.act/status/`
   (completion signal), `.act/followups/` (spawns), and the JSONL transcript
   (enrichment). Atomic-write + consume-on-ingest conventions apply.
4. **Process source** — ACT's own process supervision (always ACT-internal, not
   agent-provided): non-zero exit → `error`; watchdog timeout → `stale`.

**Example compositions** (adapter's choice, freely mixable):
- *Claude Code:* HTTP hooks (lifecycle) + file source (status / follow-ups /
  JSONL enrichment) + process source.
- *Codex:* command-hook forwarder + file source + process source.

### Relationship to the stream-json control channel

In managed mode (see Liveness & input channel) the live **control stream** is
itself a source — and the authoritative one for anything needing a synchronous
reply: **permission requests, questions / AskUserQuestion, and hook callbacks**
arrive as control-requests on the stream, not via the `Notification` hook. The
HTTP-hook / command-forwarder / file / process sources cover fire-and-forget
lifecycle events and enrichment. The exact split of which lifecycle events come
via the stream vs. hooks is an adapter/build detail (tied to the underdocumented
protocol caveat) — the rule of thumb: **stream for anything ACT must respond to,
hooks/files for anything fire-and-forget.**

### Session identity / correlation

Every hook payload includes `session_id`, `transcript_path`, `cwd`, and
`hook_event_name` (per-turn events also add `permission_mode`, `effort`). So
correlation is deterministic:

- `session_id` → look up the card by the `sessionId` ACT **pre-minted** at
  launch. Exact.
- **Unknown `session_id`** (a session ACT didn't launch) → **ignore** (ACT is
  orchestrator-only).
- `transcript_path` → the exact JSONL to tail for enrichment (no path
  reconstruction).

### Reliability

- Hooks are **synchronous and block the agent** until they return (with
  timeout). ACT's endpoint must **accept-and-return instantly**, processing
  asynchronously, so it never slows the agent. (Claude Code also allows a hook
  to background itself via `{"async":true}`.)
- **Per-card event serialization** — process one card's events in order; handle
  concurrent POSTs and LiteDB write concurrency.

### Hook injection (no clobbering)

ACT registers its hooks via a **dedicated settings file passed at launch**,
never by editing the user's committed `.claude/settings.json` — keeping ACT's
instrumentation isolated from the user's own hooks. *(Exact flag/mechanism to
confirm at build.)*

### Registered events → normalized events

- `SessionStart` → capture `transcript_path`, confirm binding
- `PreToolUse` / `PostToolUse` → activity (`running`)
- **permission requested / question** → via the **control stream** in managed
  mode (see above); `Notification` (Claude Code) / `PermissionRequest` (Codex)
  hooks are the fallback signal
- `PreCompact` → compacting
- `Stop` → turn ended → read status file → route (to review / needs answer)
- `SessionEnd` → session ended (persistence)
- process exit code (process source, not a hook) → `error`

### Edge cases

- **ACT-restart recovery** — binding persists in LiteDB (`sessionId` on the
  card), so re-correlation after ACT restarts is trivial; `transcript_path`
  persists too.
- **Resume/fork** — a known `--session-id` / `--resume` keeps the binding; a
  Codex fork producing a *new* id is the deferred `sessions[]` case.

---

## Rules engine

Drives all transitions in the machine-controlled region. Operates on
**normalized events** — each adapter maps its agent's raw hooks (Claude Code
`Stop` / `Notification` / `PreToolUse` / …; Codex's set) into ACT's shared
vocabulary, and the table is written against that. Two transitions are **not**
event-driven: **Ready → Executing** is ACT-initiated (it spawns the process — an
action, not a rule), and **To review**'s exits are human (Complete / Send back).

| Current | Normalized event | → Column | Badge |
|---|---|---|---|
| Executing | activity (tool use / turn progress) | Executing | `running` |
| Executing | compacting (`PreCompact`) | Executing | `compacting` → `running` |
| Executing | permission requested | Needs feedback | `needs permission` |
| Executing | turn ended + status `needs_input` | Needs feedback | `needs answer` |
| Executing | turn ended + status `ready_for_review` | To review | `idle` |
| Executing | turn ended, no/invalid status file | To review | `idle` *(fallback)* |
| Executing | process exited non-zero / crash | Needs feedback | `error` |
| Executing | no-activity timeout | *(stays)* Executing | `stale` |
| Needs feedback | user input supplied | Executing | `running` |

### Completion signal — the status-file convention

Resolves "clean `Stop` vs. question `Stop`" with an explicit signal instead of
guesswork, reusing the same agent→ACT file mechanism as follow-ups:

- **Injected preamble.** At session start ACT prepends a small instruction block
  to `initialPrompt`, telling the agent how/where to write a **completion
  signal** before ending each turn.
- **Status file**, written atomically (temp → rename), in a known dir — a
  sibling of the spawn dir, e.g. `.act/status/<taskId>-<turn>.json` (the task's
  own `id`, so it never collides with `.act/followups/` or other tasks).
- **Format:** `{ state: "ready_for_review" | "needs_input", question?: string }`.
  `needs_input` carries the question text (shown on the card); `ready_for_review`
  = done, nothing blocking.
- **Read on `Stop`.** `needs_input` → Needs feedback (`needs answer`);
  `ready_for_review` → To review (`idle`).
- **Fallback:** missing or malformed file → **To review** (never trap a finished
  task in limbo).
- **Caveat:** unlike an enforced `PermissionRequest`, this relies on the agent
  obeying the preamble; the fallback absorbs misses, and the preamble wording is
  something to tune over time.

### Other resolutions

- **`error` / `stale` come from process signals, not hooks.** ACT owns the
  spawned process: non-zero exit → `error` (→ Needs feedback); watchdog with no
  activity → `stale`. (`stale` applies only *during* a live turn, since no
  process runs between engagements.)
- **`stale` does not auto-escalate.** Warning badge only; card stays in
  Executing; escalation is the user's call via the kill/abandon hatch.
- **`permissionMode` changes frequency, not the table.** `bypass` ⇒ "permission
  requested" rarely fires (cards sail to `Stop`); `default` ⇒ fires often.

### Error handling & retry

Any `error` in Needs feedback is **manually retriable** — a **Retry** action in
the drawer re-attempts the task (via `--resume <sessionId>` to continue where it
failed; fresh-seeded from `initialPrompt` if the transcript is gone) → back to
Executing. Error kinds and what retry means:

- **Transient / environmental** — *model not available*, *out of tokens /
  rate-limited* mid-run, network. Retriable once the cause clears. For these the
  drawer lets the user **edit `launchConfig` first** (e.g. switch `model`) then
  Retry; for a limit hit, Retry becomes available once the window resets
  (optionally auto-retry — ties to scheduling backpressure, but default is
  manual).
- **Execution** — crash / non-zero exit / tool failure. Retriable (resume), may
  warrant investigation first.
- **Inline git failure** (auto-complete) — retriable the same way.

So the drawer's action set is state-specific: permission → approve/deny;
question → answer; **error → Retry (± edit launch config)**; review →
send-back / complete / kill. Kill/abandon is available on any active card.

### Auto-complete (fire-and-forget)

For tasks with `autoComplete` set, the "turn ended clean" branch changes:

- Executing + `ready_for_review` **and** `autoComplete`:
  - if `autoGit` was configured, the git actions were injected into the launch
    prompt, so they already ran in-session — the card goes straight to
    **Completed**.
  - if the in-session git step failed → **Needs feedback** (`error`), not
    Completed (never complete with broken git).
- Executing + `ready_for_review` **without** `autoComplete` → **To review**
  (normal human sign-off).
- `autoComplete` never bypasses **error / `needs_input` / permission** — those
  still route to Needs feedback and wait for the user. (Pair with
  `bypass`/`acceptEdits` `permissionMode` for truly hands-off overnight runs.)

Control arc for these tasks: human → machine → **auto-done** (final human step
dropped by opt-in; launch boundary unchanged).

### Agent ↔ ACT contract

The two `.act/` conventions — **follow-ups** (`.act/followups/`) and **status**
(`.act/status/`) — together form the agent→ACT contract, both taught by the
injected preamble. Worth documenting as one thing when built.

---

## Liveness & input channel

**Single mode: managed.** ACT spawns and drives the agent session itself. No
desktop-handoff mode, no GUI automation (both rejected — see below).

### Live during a turn, resume between

While a task is actively engaged (Executing, or Needs feedback awaiting the
user's answer) ACT keeps the agent process **alive** and interacts with it over
a structured channel. When idle / queued / To review / Completed, **no process
runs** — state lives in the on-disk transcript, and work resumes via
`--resume`. Keeps unattended/scheduling/crash-resilience while allowing mid-turn
interaction.

### Transport: stream-json control protocol over stdin/stdout

ACT spawns `claude -p --input-format stream-json --output-format stream-json`,
holds the pipes open, and speaks the newline-delimited JSON **control
protocol**: prompt / user messages in; agent events + **control-requests**
(permission, questions / AskUserQuestion, hook callbacks) out; **control-
responses** (approve / deny, answers) in. This is the same transport the Agent
SDK uses under the hood, so it's **.NET-native** — no Node/Python SDK sidecar,
no MCP permission server, no PTY.

- **Rejected alternative — literal PTY:** driving the interactive TUI via a
  pseudo-terminal with injected keystrokes + ANSI screen-scraping is fragile,
  version-brittle, and cross-platform-painful (esp. Windows). The stream-json
  channel gives the same "live process, answer over stdin" behavior, structured
  and robust.
- **Caveat:** the CLI stream-json control protocol is **underdocumented** (open
  Anthropic issue) though SDK-proven — pin the Claude Code version and verify
  the protocol at build. Codex has no confirmed equivalent; its adapter
  implements the input channel its own way (its explicit `PermissionRequest`
  event, etc.). The channel is an **adapter responsibility**, per the ingestion
  abstraction.

### The input channel = this stream

Send-back (To review → Executing), permission grants, and question answers all
flow as control-responses / user-messages over the live stream. After teardown,
reopen / send-back re-spawns via `--resume <sessionId>` (a fresh stream-json
process).

### Kill / abandon hatch (resolved)

ACT owns the process, so **kill = terminate it** (the stream also supports
interruptions) → route the card to Needs feedback with an `error` / `killed`
badge. Not a manual column move — a *session action* that triggers an auto
transition, so the launch-boundary rule holds.

### Rejected: handoff & desktop automation

- **Handoff (desktop deep-link) mode** — `claude://code/new?q=…` **prefills but
  never auto-sends** (deliberate safety), so it's attended-only and can't do
  unattended/overnight; also loses metrics (desktop store is opaque/binary) and
  reliable session reopen. Rejected in favor of one managed model.
- **Desktop GUI automation** (synthetic Enter / Playwright) — rejected:
  Electron's single-instance lock strips `--remote-debugging-port` (breaks
  Playwright/CDP attach); synthetic keystrokes need OS Accessibility permission
  and fight focus/timing races, and would have to auto-click every
  permission/question dialog too. It only "helps" the unattended case — which
  managed mode already does cleanly.

---

## Scheduling & queue policy

Replaces any attempt to *estimate* task effort (unreliable — agentic cost is
unpredictable and remaining budget isn't cleanly readable). Instead the user
sets **per-task intent** for *when* a task may start.

### Per-task `schedule`

Set at the Preparing → Ready transition; editable while in Ready:

- **Manual** — never auto-launched; user starts it by hand.
- **Now** — as soon as possible (subject to cap + backpressure below).
- **Next 5-hour window** — at the next session-window reset.
- **Window after next** — one reset later.
- **Specific date & time** — user-picked datetime.

### Runner logic

A task becomes **eligible** when its `schedule` condition is met, then launches
only if **both**: the concurrency cap has a free slot, **and** its `dependsOn`
prerequisites are Completed.

- `maxConcurrent` — cap on tasks past the launch boundary and not yet in To
  review/Completed (i.e. **Executing + Needs feedback** — blocked-but-alive
  sessions count).
- **Rate-limit backpressure (safety net)** — if an eligible task's usage window
  is exhausted, it **waits for reset** (shown as `waiting-reset` in Ready)
  rather than erroring. Distinguish the two limits: 5-hour window → wait hours;
  weekly cap → wait days (don't retry hourly against a weekly lockout). So
  `schedule` is *intent*; cap + backpressure are *reality*.

### Spawned-task schedule defaults

- **ACT-emitted** (git commit/push/PR): default **Now**; user can override the
  schedule right in the To review → Completed git modal.
- **Agent-emitted** (plan follow-ups): default **Manual** (nobody chose them at
  spawn time → review-before-run is safer). *(Revisit if inherit/now preferred.)*

### Master switch

A global **"pause all auto-execution"** toggle (auto-execution on by default).
When paused, no task auto-launches regardless of its `schedule`; per-task
**Manual** remains the per-task opt-out. Manual launches still work while paused.

### Ready-card indicators

Ready cards show their **`schedule` as a badge** (e.g. `manual`, `now`,
`next window`, `⏱ Mar 3 09:00`), and a `waiting-reset` badge when
backpressure-paused. (Pre-launch, so no *execution* badge yet — these are
scheduling badges specific to the Ready column.)

### Keep-awake (prevent sleep)

A configuration option to **prevent the computer from sleeping** while
auto-execution is active — otherwise the overnight queue stalls when the machine
sleeps. OS-specific under the hood (Windows `SetThreadExecutionState` / Linux
`systemd-inhibit` / macOS `caffeinate`); exposed as one ACT setting. Should also
be respectful — only inhibit sleep when there's pending/active auto-work, and
release it otherwise.

### Build-time dependencies (not blockers)

- **"Next window" needs the 5-hour reset time.** It's rolling (~5h after the
  session's first message) — compute from tracked activity or read `/usage`.
- **Weekly reset** = a fixed per-account day/time, configured once.
- **Unattended overnight runs need the right `permissionMode`** (`acceptEdits` /
  `bypass`), or tasks stall in Needs feedback waiting for permission all night.
- Verify at build: `/usage` scriptability, and clean mid-task resume after a
  rate-limit interruption.

---

## Persistence & archival

Two threads with different answers: data retention, and session lifecycle.

### Data retention — retain, don't destroy

Local single-user app, so storage isn't a real constraint; the bias is to keep
history.

- **Active board** shows all non-Completed cards plus Completed ones within a
  recent window.
- **Auto-archive** Completed cards after a **configurable window (default 90
  days)**: they leave the board but stay in LiteDB — fully searchable, with
  `transitions[]`, `metrics`, lineage, and `initialPrompt` intact (audit +
  analytics value, cheap to keep).
- **Purge = manual only.** No auto-delete. A manual delete exists on any card,
  with two guards: deleting a card with children must not orphan lineage (block
  or cascade-confirm), and deleting an **active** (past-launch) card requires
  killing its session first (cross-refs the kill/abandon hatch).
- **Artifact housekeeping:** on completion/archival ACT cleans up **its own**
  `.act/status/` and `.act/followups/consumed/` files for that task. It **never**
  touches the agent's transcripts (not ACT's to delete, and needed for resume).

### Session lifecycle — resumability rides the transcript

Resumability does **not** depend on ACT keeping anything alive. Send-back
(To review → Executing) and reopen (Completed → To review) work via `--resume
<sessionId>` against the agent's **on-disk transcript**, which persists
independently of ACT.

- Per the liveness model, ACT keeps the process alive only **during an
  active turn** and tears it down on entering To review / Completed / idle; it
  re-spawns via `--resume` when work continues. So between engagements there is
  no process to manage.
- **Caveat:** the transcript is outside ACT's control, subject to the agent's
  retention or user deletion — so a very old Completed task may no longer be
  resumable.
- **Fallback:** reopen/send-back attempts `--resume`; **on failure, offer to
  start a fresh session seeded with the stored `initialPrompt`** (and possibly
  `lastMessage`) rather than failing silently. Record which path was taken.

---

## Git integration

Optional git handoff on the **To review → Completed** transition. The completion
modal prompts for an optional action: **commit**, **push**, **create PR** (with a
**draft** checkbox). Actions chain (commit → push → PR; draft modifies the PR
only); "no git action" is always available.

- **Mechanism:** the completing task simply **completes**, and ACT **spawns a new
  git task in Ready** (ACT-emitted — ACT writes its prompt from the user's
  choice) to do the commit/push/PR. No Executing round-trip on the parent; the
  git work is its own tracked card that can hit Needs feedback on its own — just
  the general task-spawning mechanism applied to git. The completion modal also
  offers the spawned task's `schedule` (default **Now**).
- **Contrast — auto-complete tasks:** when git is known at **creation** (`autoGit`
  with `autoComplete`), it's injected into the launch prompt and done
  **in-session** — no follow-up task. Spawning applies to git chosen at **review
  time**, which wasn't known at launch.

---

## Native OS notifications

Load-bearing, not cosmetic: when you're not watching the board (especially
overnight), the OS notification **is** the "needs you" signal. Without it,
unattended mode is half-blind.

### Events → triggers (map to attention-state transitions)

- **Needs feedback** — `needs permission`, `needs answer`, `error` (blocked on
  you). Always on.
- **To review** — a task finished and wants sign-off.
- **Completed (auto)** — an `autoComplete` task closed itself (the overnight
  "it's done" ping).
- **`waiting-reset` / rate-limited** — queue paused until the window resets, and
  again when it resumes.
- **`stale`** — optional "possibly hung" heads-up.

### Behavior rules

- **Actionable.** Where the OS supports it, notifications carry buttons that
  deep-link into that card's drawer — approve/deny on a permission ping,
  "review" on a To-review ping.
- **Respect focus.** Suppress the popup when ACT is focused and on the board
  (the in-app pulse covers it); notify mainly when ACT is backgrounded — the
  overnight case.
- **Coalesce, don't spam.** Batch bursts into a digest ("4 completed, 1 needs
  you"); never re-fire the same state for the same task.
- **Configurable per event type** — a small settings matrix (e.g. error/needs-you
  always on; "completed" maybe off by day, on overnight). Stored as a
  notification-preferences setting.

### Shell caveat (build note)

Full native notifications are effectively a **desktop-shell (Electron)
capability**. Under a plain `dotnet run` browser tab you'd be limited to Web
Notifications (permission grant required, unreliable when backgrounded) — another
point for the Electron packaging.

---

## UI direction

Direction settled; only pixel-level polish (final spacing, type, Radzen theming)
remains for build time. Reference: `act-ui-preview-v2.html`.

- **Aesthetic:** **air-traffic control room.** Cards are **flight-progress
  strips** — colored status-light rail on the left edge, callsign-style id +
  working-dir path in mono, title in a technical sans. Dark control-room palette
  (deep blue-slate bg; semantic status colors: teal=running, amber=needs
  permission/answer, red=error, blue=idle/to-review, muted green=done, dim
  yellow=stale). Brand mark = a small radar sweep.
- **Board:** **six flat columns — no persistent zones.** The launch-boundary
  rule is shown **dynamically at drag time**: picking up a draggable card lights
  only its valid drop targets and **grays out invalid columns** (Ready →
  Preparing + Executing; Completed → To review). Machine cards (Executing / Needs
  feedback / To review) don't lift (can't be hand-moved).
- **Density toggle:** **compact** (id + title + **badge** + schedule chip — badges
  shown, not just a dot) vs **spacious** (fuller strip with metrics, path,
  lineage). Top-bar toggle, persisted.
- **Attention:** needs-you cards get a **loud in-place treatment** (glowing rail
  + pulse + `!` corner); no separate inbox. Top-bar **"N need you ›"** pill
  summarizes *and* jumps to the next attention card.
- **Interaction:** one **contextual right-side drawer** over a dimmed board,
  action set by state — permission → **Approve / Deny** (kept **simple**: brief
  statement of the request, no command dump or scope radios); question → answer;
  error → **Retry** (± edit launch config); review → Kill / Send back / Complete.
  Kill on any active card.
- **New task:** a **modal** with the control set (title, prompt, dir, agent,
  model, effort, permission mode, schedule, auto-complete, auto-git; tools / env
  / flags behind **Advanced**) and a **single Save** → lands in Preparing; the
  user drags it onward.
- **Build split:** signature flight-strip look = custom Blazor markup + CSS;
  heavier widgets (dialog/modal, drawer, tables, inputs) = Radzen themed to the
  same palette via shared CSS variables. Mockups were hand-CSS only.
- **Settings page:** a dedicated user-settings screen (built in roadmap step 16)
  is the home for scattered preferences. **Theme** lives here — Radzen's
  *Standard* and *Standard Dark* themes, with the **default following the OS**
  light/dark preference (wired at Radzen setup via `prefers-color-scheme`; the
  settings page later adds an explicit override). Also gathers density default,
  the notification matrix, keep-awake, `maxConcurrent`, weekly-reset time, and
  the auto-archive window / auto-execution pause.

---

## Spec status

This section is the resume point. The design is **complete** — nothing left to
define. The build sequence lives in **`ACT-roadmap.md`** (16 steps across 6
phases). Only pixel-level UI polish and per-step build-time verifications remain,
tracked there.

---

## Known constraints to carry forward ⚠️

- CLI-launched sessions are **not** visible in the official Claude desktop app
  (separate session stores, post-April-2026 redesign); ACT is the visibility
  surface. Prefer explicit `--session-id` / `--resume <id>` over `--continue`.
- Electron.NET no-CLI path is pre-release; Node.js 22.x needed on the build
  machine; Linux builds from Windows need WSL2.

---

## Future enhancements 🚀

- **Embedded git** — instead of spawning an agent task (or an in-session turn)
  for git, ACT runs the git / host-CLI operations **itself** to save tokens (a
  commit is deterministic work not worth spending LLM tokens on). An optimization
  over the spawned/in-session git integration, which is the default.
- **Task templates / presets** — save a reusable launch config + prompt skeleton
  (e.g. "bugfix on repo X with these tools/model/permission mode") so creating a
  common task is one click instead of filling every field.
- **Remote access** — check in on the board from a phone while away (the overnight
  use case begs for it): at minimum a read-only view, ideally approve/deny a
  permission or answer a question remotely. Security-sensitive — needs auth and a
  safe transport.
- **Recurring / cron tasks** — schedule a task to run on a repeating cadence (e.g.
  "every morning, run the dependency-update task"). Small leap from the existing
  scheduler, which already understands time.
