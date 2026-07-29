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
| Terminal | **Porta.Pty** (ConPTY / Unix pty) + **xterm.js** | ACT hosts the agent's real interactive TUI. Raw bytes both ways over the Blazor circuit, batched ~30 ms with a capped buffer — fine on localhost. See Liveness & the embedded terminal. |
| Ingestion | **Pluggable multi-source** — HTTP/command hooks, `FileSystemWatcher`, process signals | Adapters compose a mix per agent; all normalize into one event stream. Purely **observational** — see Ingestion & session identity. |
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
  Blazor, Radzen.Blazor, Electron.NET, LiteDB, Serilog, Porta.Pty, xterm.js, etc. —
  so they combine freely into a work distributed under ACT's chosen source-available
  license. **No GPL/AGPL dependencies** (would conflict with a non-open license).
  `Porta.Pty` and xterm.js are both **MIT** and are recorded in the NOTICES file.
- **Vendored files carry an extra duty.** xterm.js ships as committed `dist` files
  rather than a package reference — deliberately, so the app needs neither Node nor a
  CDN at runtime — which means nothing resolves or records its version for us. A
  `VENDOR.md` beside the files pins the version and SHA-256 of each, and carries the
  script that re-verifies them against the published package. Do the same for anything
  else vendored: an un-versioned blob in `wwwroot` is a supply-chain hole.
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
  odd, load-bearing bits (the PTY's incremental UTF-8 decode and batched flush,
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

> **Liveness model** (see Liveness & the embedded terminal). ACT spawns the
> agent's **interactive TUI under a pseudo-terminal it owns** and keeps that
> process **alive for as long as the card is active** — through Executing, Needs
> feedback and To review alike — tearing it down only on kill or completion. So
> "working" = the TUI is mid-turn; "idle" (To review) = the same live TUI parked
> at its prompt; "send input" = **the user typing into that terminal**, which ACT
> hosts full-screen for the card.

**4. Needs feedback** *(machine-controlled)*
The session is blocked and needs the user; the badge says why: a permission
prompt → **needs permission**; a question → **needs answer**; crash / non-zero
exit → **error**. Failures route here (not To review). The card is a *report*
that the TUI is waiting — **ACT never answers on the user's behalf** (see Hooks
are observability, not control). Exits: for permission/question, the user opens
the card's terminal and answers there, and ACT sees the session move again → back
to Executing (badge returns to `running`); for **error**, the user can **Retry**
(see Error handling & retry) → back to Executing.

**5. To review** *(machine-controlled → hands back to human)*
The agent finished its turn cleanly (`Stop`, no question, no error); badge goes
`idle`. Work is produced and nothing is blocking; it is the user's turn to
inspect it. The TUI is still alive, parked at its prompt. Exits: **Completed**
(approve/finish) or **Executing** (send-back — user feedback re-enters the same
session by being typed into that same terminal).

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
- `sessionId` — the **agent's** session id. **Single value; null until launch** —
  and, for some agents, for a little longer. This is the join key for inbound hooks
  and JSONL transcripts. Two acquisition modes, because the agents genuinely differ:
  - **Pre-minted** (Claude Code) — ACT generates the UUID and passes `--session-id`,
    so the binding exists before the process does.
  - **Reported** (Codex) — the CLI has no `--session-id`; it mints its own and
    tells ACT in the `SessionStart` hook payload. `sessionId` stays null for the
    first moments of the session, and correlation until then is by **ACT's own task
    id**, carried on the launched process's environment. Resume still takes the id
    (`codex resume <uuid>`), so only acquisition differs, not use.
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
  window-after-next | datetime`. Set at **creation** (new-task modal, default
  `manual`) and editable in Ready; only *displayed* as a badge in the Ready column
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

- `workingDir` — the task's cwd. **Stored as the user typed it and resolved at launch:** a
  leading `~` is expanded (nothing below a shell does it, so `~/dev/act` would otherwise
  reach `CreateProcess` verbatim and fail), separators are normalized, and the directory
  is checked to exist *before* spawning — a pty reports a missing one as "The directory
  name is invalid" tacked onto the entire command line, which buries the only fact that
  matters. The card keeps showing the short form. The **task form** runs the same check
  as you type and offers to create the directory, so the commonest launch failure is
  caught where it can still be fixed rather than at launch. Resolution is the
  `IWorkingDirectories` port, since the launch, the form and step 8's `.act/` watchers
  all need the same answer. Its placeholder names the two ways in ("Type a path, or
  browse") rather than showing a sample path — a greyed `C:\dev\act` reads as a value
  the field already holds, and the format it was teaching is better taught by the
  validation message on the rare occasion it is wrong. The field is **type-or-pick**: a
  **Browse** panel walks the
  machine's own directories — rooted at the drives on Windows, `/` elsewhere, so one
  picker serves both. It browses server-side deliberately: a browser will not hand back an
  absolute path (the File System Access API withholds it by design) and a native dialog
  exists only under the Electron shell, so neither works in both of ACT's run modes.
  Validation distinguishes **malformed** (blocks the save — including a *relative* path,
  which would otherwise resolve against wherever ACT happens to be running) from merely
  **missing** (a warning plus *Create it*, and the task still saves, because a directory
  you are about to make is a reasonable thing to plan against).
- `agentBinary` — path to the executable (global default per `agentType`). Resolved
  against `PATH` at launch, since a pty spawns with an explicit image path and does not
  search for one.
- `model` — per-task; **agent-specific** values (e.g. current Claude Code:
  Sonnet 5 / Opus 4.8 / Fable 5).
- `effort` — per-task reasoning effort; **agent + model-specific**. Not a flat list
  per agent: Codex's catalog gives each model its own ladder (`gpt-5.6-terra` reaches
  `ultra`, `gpt-5.6-luna` stops at `max`, `gpt-5.5` at `xhigh`), so capabilities are
  modelled as **models each carrying their own efforts and default**, and the
  new-task modal narrows the effort list when the model changes. Some models ignore
  effort entirely, so it may still be a no-op.
- `permissionMode` — **first-class normalized field**, fixed set ACT understands:
  `default | plan | acceptEdits | auto | dontAsk | bypass`. Adapter-mapped
  (Claude Code: `--permission-mode …`; ACT's `bypass` maps to that CLI's
  `bypassPermissions`). Verified against the installed CLI, whose own choices are
  `acceptEdits | auto | bypassPermissions | default | dontAsk | plan`, resolving
  to these decisions:

  | Mode | Decision when a tool needs permission |
  |---|---|
  | `default` | **ask** the user |
  | `plan` | no tool execution at all (read-only) |
  | `acceptEdits` | ask, except edits, which are auto-allowed |
  | `auto` | **classify** — a model classifier approves or denies, no prompt |
  | `dontAsk` | **deny** unless pre-approved by a rule — never prompts |
  | `bypass` | allow everything (needs `--allow-dangerously-skip-permissions`) |

  **Codex maps the same set onto two axes** — `-a/--ask-for-approval` crossed with
  `-s/--sandbox` — which is why `permissionMode` is normalized in ACT rather than
  passed through:

  | ACT mode | Codex flags |
  |---|---|
  | `default` | `-a untrusted -s workspace-write` |
  | `plan` | `-a never -s read-only` (no mutations possible) |
  | `acceptEdits` | `-a on-request -s workspace-write` |
  | `auto` | `-a on-request -s workspace-write` *(the model decides when to ask — Codex's closest analogue to a classifier)* |
  | `dontAsk` | `-a never -s workspace-write` (failures returned to the model, never prompts) |
  | `bypass` | `--dangerously-bypass-approvals-and-sandbox` |

  `auto` and `acceptEdits` land on the same flags, so the Codex adapter records an
  **adjustment** for `auto` rather than pretending it honoured a distinct mode —
  the never-silently-drop rule applied to a genuine overlap.

  - **This is a state-machine knob, not just config.** It governs how often a card
    enters Needs feedback: `default` ⇒ often; `acceptEdits` ⇒ less; `auto`,
    `dontAsk` and `bypass` ⇒ effectively never for permission, because none of
    them prompt. Note the difference that matters for unattended runs: `bypass`
    silently allows, while `dontAsk` silently **denies** — a `dontAsk` task never
    stalls overnight but may finish having been blocked from work it needed, so it
    trades a stalled card for a possibly incomplete one. `auto` sits between
    them, delegating the call to a classifier.
  - `plan` (read-only, no mutations) is the natural fit for a **planning task**
    that emits follow-ups without touching anything — pairs directly with the
    spawn/plan-decomposition pattern.
- `allowedTools` / `disallowedTools` — optional tool allow/deny.
- `extraFlags` / `env` — escape hatch for anything not modeled.

*Precedence caveat:* ACT's per-task flags layer **on top of** the project's and
user's existing `settings.json` (CLI flags > project > user > built-in
defaults). A project deny list still applies underneath — ACT does **not** fully
own permissions.

---

## Ingestion & session identity

### Pluggable, multi-source ingestion

Ingestion is **transport-agnostic** and, since the PTY decision, **entirely
observational** — every source reports, none of them commands. ACT defines source
types; each **agent adapter composes whatever mix of sources fits its agent** —
including several at once for a single agent. All sources normalize into **one
event stream** that the rules engine consumes. The rules engine never learns *how*
an event arrived, only its normalized type — so adding a transport is adding a
source, with no downstream change.

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
4. **Process source** — ACT's own process supervision of the PTY (always
   ACT-internal, not agent-provided): non-zero exit → `error`; watchdog timeout →
   `stale`.

**Example compositions** (adapter's choice, freely mixable):
- *Claude Code:* HTTP hooks (lifecycle) + file source (status / follow-ups /
  JSONL enrichment) + process source.
- *Codex:* command-hook forwarder + file source + process source.

Note what is **not** a source: the terminal itself. ACT pipes the PTY's bytes to
xterm.js and never reads them for meaning (see *ACT never parses terminal output*).

### Hooks are observability, not control

**ACT observes prompts; it never answers them.** When the agent asks for
permission or asks a question, that arrives as a fire-and-forget hook
(`Notification`, and `PreToolUse` for the tool about to run). ACT normalizes it,
moves the card to Needs feedback with the right badge, and shows a **read-only**
summary of what is being asked. Answering happens where the prompt actually lives:
in the TUI, in the card's terminal, typed by the user.

This is what makes the "hooks must never slow the agent" rule strictly true rather
than aspirational — ACT has no decision to make, so it can accept-and-return
immediately in every case. It also means no ACT bug can approve something on the
user's behalf.

- **Caveat (build-time).** Exactly what Claude Code's `Notification` hook fires for
  on the pinned version is unverified, and it is the load-bearing signal for the
  `needs permission` badge. If it proves too coarse, the fallback is `PreToolUse`
  plus the absence of a matching `PostToolUse` within a short window — a heuristic,
  and one to write down as such rather than hide.

### Session identity / correlation

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
- `UserPromptSubmit` / `PreToolUse` / `PostToolUse` → activity (`running`).
  `UserPromptSubmit` is also the **recovery** signal: it is how ACT learns the user
  answered a prompt in the terminal, since no answer passes through ACT.
- **permission requested / question** → `Notification` (Claude Code) /
  `PermissionRequest` (Codex), read-only
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
- **ACT restart kills live terminals.** The PTY is a child of ACT's process, so
  restarting ACT ends every session it was hosting. The binding survives (it is in
  LiteDB), so the cards come back and each can be resumed with `--resume` into a
  fresh terminal — but the on-screen scrollback does not. Worth surfacing on the
  card rather than silently showing an empty terminal.

---

## Rules engine

Drives all transitions in the machine-controlled region. Operates on
**normalized events** — each adapter maps its agent's raw hooks (Claude Code
`Stop` / `Notification` / `PreToolUse` / …; Codex's set) into ACT's shared
vocabulary, and the table is written against that. One transition is **not**
event-driven: **Ready → Executing** is ACT-initiated (it spawns the process — an
action, not a rule). **To review → Completed** is the human's explicit call in the
UI. Everything else, including recovery from Needs feedback and send-back out of
To review, reaches ACT as an *observation* — because the user acts in the terminal,
not in ACT, so ACT learns about it the same way it learns about anything else.

| Current | Normalized event | → Column | Badge |
|---|---|---|---|
| Executing | activity (tool use / turn progress) | Executing | `running` |
| Executing | compacting (`PreCompact`) | Executing | `compacting` → `running` |
| Executing | permission requested *(observed)* | Needs feedback | `needs permission` |
| Executing | turn ended + status `needs_input` | Needs feedback | `needs answer` |
| Executing | turn ended + status `ready_for_review` | To review | `idle` |
| Executing | turn ended, no/invalid status file | To review | `idle` *(fallback)* |
| Executing | process exited non-zero / crash | Needs feedback | `error` |
| Executing | no-activity timeout | *(stays)* Executing | `stale` |
| Needs feedback | activity observed *(the user answered in the terminal)* | Executing | `running` |
| To review | activity observed *(the user sent it back in the terminal)* | Executing | `running` |

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
  activity → `stale`. Since the TUI now stays alive between turns, `stale` is
  scoped to cards **in Executing** — a To-review card sitting idle at its prompt is
  the normal resting state, not a hang.
- **`stale` does not auto-escalate.** Warning badge only; card stays in
  Executing; escalation is the user's call via the kill/abandon hatch.
- **`permissionMode` changes frequency, not the table.** `default` ⇒ "permission
  requested" fires often; `acceptEdits` ⇒ less; `auto` / `dontAsk` / `bypass` ⇒
  it effectively never fires (cards sail to `Stop`), since none of those prompt.
  A `dontAsk` denial is not a normalized event — the agent absorbs it and keeps
  going, so it surfaces only in the transcript, never as a badge. Under the PTY
  model this knob matters *more*: every prompt it produces is one the human must
  walk over to a terminal and answer.

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

So the drawer's action set is state-specific — and, since ACT no longer answers
anything, mostly a way *into* the terminal: permission / question → **Open
terminal** (with a read-only statement of what is being asked); **error → Retry
(± edit launch config)**; review → send-back / complete / kill. Open terminal,
Open in Desktop and Kill are available on any active card.

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
injected preamble. **Built at step 6**, and deliberately owned by the **core**, not
by an adapter: the convention is ACT's, so `Act.Core/Agents/ActContract` owns the
paths and `Act.Core/Agents/AgentPreamble` composes the text from those same
constants. An adapter only chooses *how* the preamble reaches its agent (prepended
to the prompt, a system-prompt flag, a settings file).

As built:

| | Status | Follow-ups |
|---|---|---|
| Path | `.act/status/<taskId>-<turn>.json` | `.act/followups/<taskId>-<nnn>.json` |
| Written | before the agent ends **every** turn | optional, before the turn ends |
| Body | `{ "state": "ready_for_review" \| "needs_input", "question"? }` | `{ "title", "prompt", "cwd"?, "dependsOn"? }` |
| Missing | assumed `ready_for_review` (never trap a finished task) | nothing spawned |

- **ACT owns the prefix, the agent owns the suffix** (`<turn>` / `<nnn>`), so one
  `.act/` directory shared by several tasks in a repo can never mix their files up.
  `ActContract.FilePrefix` / `FilePattern` / `BelongsToTask` are the only places that
  knowledge lives.
- **`dependsOn` in a follow-up file lists sibling *sequence numbers*** (`["001"]`) —
  the agent cannot know the task ids ACT is about to mint, so ordering is expressed
  in the only handles it has, and step 12 resolves them to ids on ingest.
- **Atomic writes** (temp name → rename in the same directory) are stated to the
  agent explicitly, so a watcher never fires on a half-written file.
- **`autoGit` rides the same preamble.** When configured, the git actions are spelled
  out as the last work before the `ready_for_review` file, and a failed git step is
  routed back to the user as `needs_input` naming the step — never completed.

---

## Liveness & the embedded terminal

**Single mode: managed under a PTY.** ACT spawns the agent's **real interactive
TUI** in a pseudo-terminal it owns (ConPTY on Windows, a Unix pty elsewhere) and
hosts that terminal inside ACT, full-screen per card. ACT is the control tower
around the session, not a replacement front-end for it.

The division of labour this buys is the whole point:

| | |
|---|---|
| **ACT** owns | which sessions exist, where they sit on the board, what needs you, metrics, lineage, scheduling |
| **The TUI** owns | the conversation — every prompt, permission dialog, plan approval and answer |

### Alive while the card is active

The process is spawned at launch and stays **alive across turns** — through
Executing, Needs feedback and To review alike — until the user kills it or the
card completes. This is the natural shape for an interactive terminal: scrollback
survives, and the user can keep typing without ACT re-spawning anything underneath
them.

*(This reverses an earlier "live during a turn, torn down between engagements"
model, which existed to serve a headless transport that no longer applies. The
cost is real and accepted: N active cards means N live agent processes.)*

### Transport: a pseudo-terminal, raw both ways

`Porta.Pty` spawns the CLI under a pty; bytes stream to **xterm.js** in the
browser over the Blazor circuit, and keystrokes stream back. Output is decoded
incrementally (a UTF-8 sequence can straddle two reads), batched on a ~30 ms
timer, and capped — a full-screen TUI redraws far more often than a circuit wants
to be poked. Each session keeps a **bounded scrollback** so leaving the card's
terminal and coming back replays the screen instead of showing an empty one.

### ACT never parses terminal output

**Rule, not a preference.** ACT pipes the terminal's bytes and never reads them
for meaning. The prototype's `PtyStateProbe` existed to measure the alternative,
and the measurement is the reason: its regexes had to be rewritten once *within a
single CLI version bump* (the input line moved from `│ >` to `❯`), against a
screen that redraws constantly. Any state ACT inferred that way would be
version-brittle and silently wrong. **Hooks are the state channel; the terminal is
a pipe.**

### The input channel is the human at the keyboard

ACT types exactly two things into a session: the **initial prompt** at launch, and
a **send-back message** if the user chooses to seed one from the UI. Both go in as
bracketed paste followed by the agent's submit key — an adapter detail, since the
submit key is agent-shaped. Everything else — answering a permission prompt,
answering a question, approving a plan, `/`-commands — the user types themselves,
in the terminal ACT is showing them.

Consequently ACT has **no approve/deny surface at all**. See *Hooks are
observability, not control*.

### Kill / abandon hatch

ACT owns the process, so **kill = terminate the PTY** → route the card to Needs
feedback with a `killed` badge. Not a manual column move — a *session action* that
triggers an auto transition, so the launch-boundary rule holds.

### Handoff to the Claude desktop app — a per-card action

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

### Rejected alternatives

- **stream-json control protocol** (`claude -p --input-format stream-json`) — ACT
  holding the pipes and answering control-requests itself. This was the previous
  design and is now rejected for the reason that outranks its elegance: it
  **replaces the interactive session the user actually wants** with a headless
  one, forcing ACT to re-implement every prompt surface the TUI already renders
  well — permission dialogs, AskUserQuestion, plan approval, `/`-commands — and to
  keep re-implementing them as the CLI evolves. It is also underdocumented (open
  Anthropic issue), so each of those re-implementations would be pinned to an
  unversioned protocol. Kept on the table as a **possible future unattended mode**,
  where there is no human to hand a terminal to and re-implementing prompts is
  moot; see Future enhancements.
- **Desktop-only handoff as the launch mode** — `claude://code/new?q=…` prefills
  but **never auto-sends** (deliberate safety), registers none of ACT's hooks, and
  writes to an opaque store. A card launched that way would have no badges, no
  metrics and no completion signal. Hence handoff is an *action on an observed
  session*, not a way to start one.
- **Desktop GUI automation** (synthetic Enter / Playwright) — unchanged rejection:
  Electron's single-instance lock strips `--remote-debugging-port` (breaks
  Playwright/CDP attach); synthetic keystrokes need OS Accessibility permission and
  fight focus/timing races. It only "helps" the unattended case, which a
  non-prompting `permissionMode` handles cleanly.

---

## Scheduling & queue policy

Replaces any attempt to *estimate* task effort (unreliable — agentic cost is
unpredictable and remaining budget isn't cleanly readable). Instead the user
sets **per-task intent** for *when* a task may start.

### Per-task `schedule`

Chosen in the new-task modal (default **Manual**) and editable while in Ready, so
moving a card to Ready needs no extra prompt. Shown as a badge only once the card
is in Ready — it is launch intent, and nothing launches from Preparing:

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
- **Unattended overnight runs require a non-prompting `permissionMode`** (`auto`,
  `dontAsk` or `bypass`). This is now a hard requirement rather than a strong
  suggestion: since ACT never answers a prompt, a prompting mode leaves the task
  parked at a TUI prompt that nobody is awake to answer, all night, holding a
  concurrency slot. `acceptEdits` only covers edits, so it still stalls on the
  first `Bash` call it wants approval for. Worth warning about in the new-task
  modal when `schedule` is unattended and `permissionMode` prompts.
- **`maxConcurrent` now caps live terminals too.** Every card past the launch
  boundary holds a live agent process, so the cap is a real resource ceiling, not
  just a politeness setting.
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
  analytics value, cheap to keep). **Not the same axis as `deletedAt`** — that marks
  what the *user* removed and expects to find in the archive; auto-archive is a
  retention policy on cards nobody deleted, and step 15 gives it its own marker rather
  than reusing this one. Sharing a field would make "restore" mean two different things.
- **Delete is always soft.** The task page's footer carries an icon-only delete, left-
  aligned and deliberately far from Save, available in **any** column. It sets
  `deletedAt` and the card leaves the board — nothing is destroyed, so it asks nothing:
  **the archive is the undo**. The only irreversible act in the app is emptying that
  archive, and it lives there rather than on the card.
  - **Archive page** (`/archive`, from the top bar): everything deleted, newest first,
    each restorable with one click, plus **Clear archive** — the single permanent delete,
    behind an inline confirm. Restore puts a card back in the column it left.
  - **Kill before archiving.** A card with a live session tears the process down first.
    Archiving while its agent kept working would leave a process editing a directory with
    nothing on the board pointing at it — precisely the state ACT exists to prevent. The
    session does not come back on restore; the card does, and can be launched again.
  - **Follow-ups are a question, not a guard.** A card with live children opens a **dialog**
    asking whether to archive them too or keep them, and it **names the follow-ups** rather
    than counting them — nobody can weigh "2 tasks". It is the one thing the user must
    decide, and separate from "are you sure", which soft delete makes unnecessary.
    **Lineage is left intact either
    way**, so an archived parent still knows its children and a restore finds them
    attached. Purging is where the both-sides rule finally applies: a purged card is
    unlinked from any surviving parent's `children[]` before the row goes.
  - **Restore is per card, never a subtree.** Children were archived by their own
    decision and come back the same way, so restoring a parent never silently resurrects
    work the user meant to be rid of.
- **Duplicate copies the intent, never the run.** Next to the task page's delete, and on
  every archive row beside Restore, a **duplicate** makes a new card carrying only what to
  run, where and how — title, prompt, working directory, agent, launch config, schedule and
  auto-complete. The copy is titled **"Copy of &lt;title&gt;"** (localised), so two cards that
  differ only by number never look identical on the board. It lands in **Preparing** with a
  fresh id and number, and with no session
  id, badge, metrics, observed model, last message or transition history: a copy is a task
  that has not started, not a fork of a session. **Lineage is dropped** — the follow-ups a
  card spawned belong to the run that spawned them, so a copy has neither children nor a
  parent. Duplicating an archived card leaves it archived; the copy is live, which is the
  point — it is how a finished or abandoned task gets run again without disturbing its record.
- **Artifact housekeeping:** on completion/archival ACT cleans up **its own**
  `.act/status/` and `.act/followups/consumed/` files for that task. It **never**
  touches the agent's transcripts (not ACT's to delete, and needed for resume).

### Session lifecycle — resumability rides the transcript

Resumability does **not** depend on ACT keeping anything alive. Send-back
(To review → Executing) and reopen (Completed → To review) work via `--resume
<sessionId>` against the agent's **on-disk transcript**, which persists
independently of ACT.

- Per the liveness model, ACT keeps the terminal alive for the whole active life
  of a card and tears it down on kill or completion. Reopening a **Completed** card,
  or any card whose terminal died with ACT, re-spawns via `--resume` into a fresh
  terminal.
- **Caveat:** the transcript is outside ACT's control, subject to the agent's
  retention or user deletion — so a very old Completed task may no longer be
  resumable.
- **Fallback:** reopen/send-back attempts `--resume`; **on failure, offer to
  start a fresh session seeded with the stored `initialPrompt`** (and possibly
  `lastMessage`) rather than failing silently. Record which path was taken.

### Where the data lives

The store is a single LiteDB file at **`%LOCALAPPDATA%\ACT\act.db`** (on Linux,
`~/.local/share/ACT/act.db`) — **outside the installation folder**, so data
survives an upgrade or a reinstall. `ACT_DATA_DIR` overrides the directory (env
var, appsettings key, or CLI arg); it's resolved once at startup and passed into
infrastructure, which never reads the environment itself.

### Single instance — one process owns the data file

**ACT is a singleton: launching it again focuses the window you already have.**
LiteDB opens the file with a single-writer lock, and two boards driving the same
agent processes and hooks would fight anyway — so a second instance is never the
right answer.

- **Enforced in the Electron main process** (`singleInstance` in the generated
  manifest, set from `<ElectronSingleInstance>` in `Act.App.csproj`): the second
  launch requests the instance lock, loses, and **quits**. `app.quit()` is async,
  so it does begin spawning its backend first — Electron tears that child down on
  exit, so it never becomes a second writer. Verified: a second launch of the
  packaged app leaves the first instance's process tree and ports untouched.
- **The first instance reveals itself** on the `second-instance` event: restore
  (if minimized) → show → focus. Wired in `Program.cs` via
  `RequestSingleInstanceLockAsync`, which also covers the window being hidden
  rather than merely minimized.
- **Consequence for development:** a browser-mode `dotnet run` and the packaged
  desktop app are separate processes with separate instance locks, so they cannot
  run at the same time against the same store — the second one to touch the file
  fails on the LiteDB lock. Point one of them at another store with
  `ACT_DATA_DIR`.

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

- **Actionable.** Where the OS supports it, notifications carry a button that
  deep-links **into that card's terminal view** — the place the user has to be to
  answer anyway. ACT carries no approve/deny buttons, in the notification or
  anywhere else.
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
  working-dir path in mono, title in a technical sans. Control-room palette —
  dark is the signature look (deep blue-slate bg), with a light variant for the
  OS light setting (see Theme below); semantic status colors are the same in
  both: teal=running, amber=needs permission/answer, red=error,
  blue=idle/to-review, muted green=done, dim yellow=stale. Brand mark = a small
  radar sweep.
- **Launch is "Launch now".** Named for what it will mean rather than what it does
  today: once the scheduler lands (step 14) a card can be waiting on a `schedule`, and
  this button is the **override** that starts it regardless. Naming it plain "Launch"
  now would have to change then, and the two would read as different actions.
- **Board:** **six flat columns — no persistent zones.** The launch-boundary
  rule is shown **dynamically at drag time**: picking up a draggable card lights
  only its valid drop targets and **grays out invalid columns** (Ready →
  Preparing + Executing; Completed → To review). Machine cards (Executing / Needs
  feedback / To review) don't lift (can't be hand-moved).
- **Column lanes:** each column is a **bay** — a faint full-height track
  (`--act-lane` fill, `--act-lane-line` hairline, rounded) that separates the
  columns and, crucially, keeps an **empty** column legible instead of collapsing
  to a floating header. An empty bay shows one muted mono line (`no cards`).
  Deliberately **not** a dashed drop-zone: nothing is ever hand-droppable in the
  four machine columns, so a drop affordance there would teach the wrong rule.
  The board background is a flat `--act-bg` — an earlier fixed-pitch vertical
  grid was dropped because its 40px pitch never aligned with the flexible column
  widths, so it read as noise rather than structure.
- **Density toggle:** **compact** (id + title + **badge** + schedule chip — badges
  shown, not just a dot) vs **spacious** (fuller strip with metrics, path,
  lineage). Chosen in the **settings dialog** (*Appearance → display mode*) and
  persisted; there is **no top-bar toggle** — see Settings dialog below. In
  compact the title ellipsizes so the badge keeps its full width: the badge is
  the signal, the title yields.
- **Attention:** needs-you cards get a **loud in-place treatment** (glowing rail
  + pulse + `!` corner); no separate inbox. Top-bar **"N need you ›"** pill
  summarizes *and* jumps to the next attention card.
- **Interaction — two surfaces, one rule.** The **drawer** is where you *read* a
  card; the **session view** is where you *talk* to it.
  - **Contextual right-side drawer** over a dimmed board, action set by state.
    Since ACT answers nothing, a blocked card shows a brief **read-only** statement
    of what is being asked (no command dump, no scope radios) plus a prominent
    **"Answer in terminal ›"**. Remaining actions: Open terminal, Open in Desktop,
    Kill, **Retry** (± edit launch config, on `error`), Send back, Complete. Open
    terminal / Open in Desktop / Kill on any active card.
  - **Session view — a full-screen route per card.** The xterm terminal takes the
    window, with a right rail carrying identity (`#1042`, cwd, agent/model) and the
    live metrics (context %, cost, turns), the same action set, and back-to-board in
    the top bar. This is the answer to every "needs you" state: the top-bar
    **"N need you ›"** pill and the OS notification both land here.
  - The terminal replays its scrollback on entry, so leaving the view and returning
    is free.
  - **Transient failures are notifications, not page furniture.** A failed launch is a
    path or a command line — it does not fit a 15rem rail, and it is news rather than a
    property of the card. It goes to the notification host; what the *card* carries is
    the `error` badge and the transition note, which persist. Anything that must survive
    a dismissal belongs on the card, not in a toast.
- **New task / edit task:** a **page**, not a modal — `/card/new` and
  `/card/{id}/edit`, matching the session view. Same control set (title, prompt, dir,
  agent, model, effort, permission mode, schedule, auto-complete, auto-git; tools /
  env / flags behind **Advanced**) and a **single Save** → lands in Preparing; the
  user drags it onward. The fields sit in a centred column so a wide window does not
  stretch them.
  - **Why a page:** a card is then always somewhere you can land, link to, and come
    back from, and its two faces — the form before launch, the terminal after — are
    the same kind of thing rather than one modal and one route. Clicking a card opens
    whichever face applies: the form in Preparing / Ready, the terminal past the
    launch boundary. **Every surface is a page** — the board, the task form, the
    terminal, the archive and settings.
  - **The title strip never dims.** The OS draws the window buttons *above* all web content, so
    a mask over the whole window dims everything except them and leaves them stranded in a bright
    block. Nothing in CSS can reach them, so the mask stops below the strip instead and the strip
    stays lit as a unit — the seam cannot form because no boundary runs through it. Its controls
    go inert while a dialog is up (navigating away would strand the dialog); the native buttons
    stay live, because closing the window must always work.
  - **Dialogs are for forks, not for places.** A page is somewhere you go and can link to;
    a dialog interrupts an action already under way and has no meaning on its own. So the
    only modals are the two destructive prompts — archiving a card with follow-ups, and
    emptying the archive — plus the completion/git prompt at step 11. Anything that is a
    *destination* is a route. Corollary learned the hard way: a decision that changes tasks
    other than the one on screen does not belong in a footer strip, however tidy.
  - **The two faces toggle, always.** Both card pages carry a `Task | Terminal` switch,
    so state decides only where a *click* lands, never what you are allowed to look at
    afterwards — you can read the prompt of a running task, or the terminal of one that
    has not launched. Consequence worth stating: reaching a terminal is not permission to
    start one. The launch action appears only for a card in **Ready** (the launch) or
    **Executing** (a re-attach after ACT restarted, where the binding survived but the
    process did not); anywhere else the page says to move the card to Ready. Otherwise
    the toggle would quietly become a way to skip Ready and break the control arc.
- **Build split:** signature flight-strip look = custom Blazor markup + CSS;
  heavier widgets (dialog/modal, drawer, tables, inputs) = Radzen themed to the
  same palette via shared CSS variables. Mockups were hand-CSS only. The terminal
  is xterm.js, themed from the same `--act-*` tokens so it reads as part of the
  control room rather than an embedded console.
- **Theme (dark / light):** both are supported and **follow the OS** by default.
  Radzen's *Standard* and *Standard Dark* stylesheets are linked behind
  `prefers-color-scheme`, and ACT's own control-room palette (the `--act-*`
  tokens: board background, flight strips, rails, top bar) ships **light and dark
  variants** switched the same way — so the whole surface flips together, with no
  JS and no flash of the wrong theme on first paint. Dark is the signature look;
  the light variant keeps the same semantic status colors at adjusted
  lightness.
- **Settings page** (`/settings`): the home for scattered preferences, opened from the
  gear in the top bar and grouped by type (**General**, **Appearance**, more to come).
  Every setting applies immediately and persists to LiteDB — no OK/Cancel, and no Close
  either: the only action is the back arrow. A page rather than a dialog for the same
  reason as the task form — see *Interaction*. It also fixes a wart the dialog had: a
  language change forces a reload, which used to dismiss the dialog as a side effect and
  dump you on the board; now you stay on settings.
  Shipped: **language**, **theme** (follow-OS / light / dark override), and
  **display mode** (compact / spacious — settings-only; there is no top-bar
  density toggle). Still to land: the notification matrix, keep-awake,
  `maxConcurrent`, weekly-reset time, and the auto-archive window /
  auto-execution pause.
- **Localization:** the UI is translatable — **English and French**, defaulting to
  the **OS language** (anything other than French falls back to English). Strings
  live in `.resx` under `Act.App/Resources/`, reached through the SDK's
  strongly-typed resource class (compile-checked keys, no extra package); adding a
  language is a new `Strings.<culture>.resx`. Radzen's own strings come localized
  with the package. The choice sets **both** cultures, so it drives formatting as
  well as text — `fr-CA` renders `0,74 $` and `3 mars 09:00`, `en-CA` renders
  `$0.74` and `Mar 3 09:00`. Changing the language **reloads the page** (the whole
  render tree must re-run under the new culture); theme and density apply in
  place.

---

## Spec status

This section is the resume point. The design is **complete** — nothing left to
define. The build sequence lives in **`ACT-roadmap.md`** (16 steps across 6
phases). Only pixel-level UI polish and per-step build-time verifications remain,
tracked there.

**Revised 2026-07-29 — the PTY reversal.** The liveness model, the input channel
and the desktop-handoff decision were all reversed on the evidence of the launch
prototype (branch `prototype`, commit `bb7633d`), which built all three routes side
by side. ACT now hosts the agent's **real interactive TUI** under a pseudo-terminal
instead of driving a headless stream-json session; hooks became a **read-only**
observability channel; and `claude://resume?session=` was confirmed and promoted
from rejected to a per-card action. Sections touched: *Liveness & the embedded
terminal* (rewritten), *Ingestion & session identity*, *Rules engine*, *UI
direction*, *Scheduling*, *Persistence*, *Native OS notifications*. Roadmap steps
7–10 were reshaped to match.

---

## Known constraints to carry forward ⚠️

- CLI-launched sessions do not appear in the Claude desktop app's own session list
  (separate stores, post-April-2026 redesign) — but `claude://resume?session=<uuid>`
  **imports one on demand**, which is the bridge ACT uses. ACT is still the
  visibility surface. Prefer explicit `--session-id` / `--resume <id>` over
  `--continue`.
- **A live card is a live process.** ACT holds one agent process per active card
  for the card's whole active life, and they all die when ACT does. Bindings survive
  in LiteDB; terminal scrollback does not.
- **The TUI is the API.** Hosting the real interface means ACT inherits its
  quirks — resize behaviour, bracketed paste, ANSI rendering — and inherits changes
  to it on every CLI release. Pin the Claude Code version and re-verify on bumps.
  The mitigation is that ACT reads *none* of it: the blast radius of a TUI change is
  the terminal looking different, never ACT misreading a state.
- Electron.NET no-CLI path is pre-release; Node.js 22.x needed on the build
  machine; Linux builds from Windows need WSL2.

---

## Future enhancements 🚀

- **Headless unattended mode** — the rejected stream-json control protocol, brought
  back for the one case where its objection does not apply: a scheduled overnight
  task has no human to hand a terminal to, so there is no interactive session to
  preserve and no prompt surface worth re-implementing (a non-prompting
  `permissionMode` means it should never prompt anyway). Would run as a second
  session kind behind the same `IAgentAdapter` seam, with no `IAgentTerminal`.
  Only worth building if PTY-hosted overnight runs prove wasteful in practice.
- **Embedded git** — instead of spawning an agent task (or an in-session turn)
  for git, ACT runs the git / host-CLI operations **itself** to save tokens (a
  commit is deterministic work not worth spending LLM tokens on). An optimization
  over the spawned/in-session git integration, which is the default.
- **Task templates / presets** — save a reusable launch config + prompt skeleton
  (e.g. "bugfix on repo X with these tools/model/permission mode") so creating a
  common task is one click instead of filling every field.
- **Remote access** — check in on the board from a phone while away (the overnight
  use case begs for it): at minimum a read-only view of which cards need you.
  Answering remotely means shipping the *terminal* to the phone (xterm.js already
  runs in a browser, so the pieces exist) rather than building the approve/deny
  surface ACT deliberately does not have. Security-sensitive — needs auth and a
  safe transport, and a remote terminal is a remote shell, so the bar is high.
- **Recurring / cron tasks** — schedule a task to run on a repeating cadence (e.g.
  "every morning, run the dependency-update task"). Small leap from the existing
  scheduler, which already understands time.
