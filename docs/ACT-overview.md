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
- **Ports as interfaces.** The agent adapter, the pseudo-terminal, the event sink
  every observer pushes into, and the store are ports behind interfaces; concrete
  adapters (Claude Code, Codex, …) and the store (LiteDB) are plug-ins. Adding an
  agent must not touch core logic.
  - **Ingestion is push, not a port of its own** — see *Pluggable, multi-source
    ingestion*.
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
  lifecycles — scheduling, spawning, sign-off — through scripted fake
  events, deterministically, no real CLI or tokens.
- **Contract tests.** One shared xUnit suite that **every** agent adapter must
  pass, so Claude Code and Codex are held to the same normalized behavior.
- **UI tests — Playwright for .NET** against the **browser-mode** Blazor app
  (`dotnet run`), driven by the **mock adapter** so flows are deterministic. Run
  against browser mode, **not** the Electron shell (single-instance-lock / CDP
  friction). Cover: task creation, drag + drag-time graying, event-driven column
  moves, and the per-state drawer actions.
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
  `to review` status fallback, error→retry, resume→fresh-seed). Surface errors to
  the card/badge, never swallow.

### Local-endpoint security

- The hook/ingestion HTTP endpoint binds to **localhost only**, on an
  **OS-assigned port ACT then keeps**, and requires a **per-session token**
  (injected into the hook config at launch) so only ACT's own launched agents can
  post to it.
- **One endpoint for every session and both agents** — a loopback endpoint on
  ACT's own web host, not a second server and not a port per session. The port is
  not the boundary: any local process can enumerate listening ports, so a port
  ACT did not publish buys collision-avoidance, and the **token** does the
  authorizing. A port per session would multiply listeners for no isolation the
  token does not already give, and would break Codex's hook-trust hash (below).
- **Sticky, not fresh each start.** ACT binds port `0` on first run, stores the
  port it got, and reuses it after that (falling back to a new one if it is
  taken). Codex hashes each hook *definition* and re-prompts for trust whenever it
  changes, so any value that moves between restarts and appears in the hook
  command string costs the user an approval prompt every launch.
- **Routed per agent** — `/hooks/claude` and `/hooks/codex`. The two CLIs' hook
  payloads are different dialects, and the path is what picks the parser, so a
  payload arriving on the wrong route fails loudly instead of being mis-parsed.
- Which card an event belongs to comes from the **payload** (`session_id`, plus
  ACT's own task id on the process environment) — never from the port.
- **The MCP server shares all of it** — same listener, same kept port, same token
  header, on `/mcp` (step 12). One difference is load-bearing: a hook payload is an
  observation ACT can afford to drop, while an MCP call **mutates** ACT's store by
  creating a card, so the token is the *only* thing that decides which card becomes
  the parent — it is never an argument the agent supplies. See *Agent ↔ ACT contract*.

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
| 3 | Executing | Machine | auto, on launch | Your turn |
| 4 | Your turn | Machine | auto: permission / question / error / `Stop` | Completed (drag) · Executing (observed input, or Retry) |
| 5 | Completed | Human | **dragged** from Your turn | Your turn (reopen) |

**Control arc:** human → machine → human. Control starts with the user
(Preparing, Ready), passes to the agent at launch (Executing, Your turn), and
returns to the user at the end (Completed). ("Control" here means who drives
movement *out of* a state. A spawned task is *created* into Ready by
machine/agent, but the user still controls its exit — gate the launch or send it
back to Preparing — so Ready stays human-controlled.)

**Launch boundary (key rule):** the moment a card enters Executing, the user
can no longer move it by hand. Columns then change *only* through automated
transitions — driven by **hooks** (in-session events) and by **process signals**
(exit code / no-activity timeout, which hooks can't report, e.g. a crash). The
exceptions are the two moves at the far end of the workflow — the **Your turn →
Completed** sign-off and the **Completed → Your turn** reopen. Neither overrides
a live session: the first *ends* one, the second re-enters the workflow. So the
rule still holds: no manual moves while a session is actively mid-flight, which
is why **Executing** is the one column that neither lifts nor accepts a drop.

**Badges (orthogonal to columns, always auto):** `running`, `needs permission`,
`needs answer`, `error`, `killed` (user-terminated via the kill/abandon hatch),
`compacting` (context being compacted), `to review` (finished, awaiting
sign-off). One event can both move a card *and* stamp its badge. `compacting`
occurs *during* Executing. There is deliberately **no `stale` badge** — a card
that has gone quiet says so beside its badge instead of in it (see *No stale
badge — the quiet chip instead*).

**Why one column and not two.** Needs feedback and To review were two columns
saying the same operational thing — *the ball is in your court* — and differing
only in **why**, which is exactly what the badge already carries. They also
behaved identically: activity observed in the terminal returned a card to
Executing from either one, under the same reason code. Merging them makes the
badge the sole carrier of the reason, and gives the column a single meaning the
attention blink and the notification rules can both key off. The consequences,
all deliberate:

- **Ordering replaces adjacency.** Two columns implied a priority by sitting
  side by side; one column has to state it. Your turn is ordered by cost of
  waiting: `needs permission` → `needs answer` → `error` → `killed` →
  `to review`. A live session parked on a prompt is burning a turn nobody is
  answering; finished work is waiting on nothing.
- **Sign-off is gated by the column, not the badge.** Any card in Your turn can
  be completed, including one that errored or was killed — previously those had
  no path to Completed except a round trip through the terminal.
- **The blink covers the whole column,** in three colours: amber for a blocked
  prompt, red for `error`/`killed`, and the calm review blue for `to review`. So
  a scan still separates "something is stuck" from "something is done" without
  reading a word.

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
is actively being worked (`running`). Auto-exit: to Your turn, whether the
session blocked or finished — the badge says which.

> **Liveness model** (see Liveness & the embedded terminal). ACT spawns the
> agent's **interactive TUI under a pseudo-terminal it owns** and keeps that
> process **alive for as long as the card is active** — through Executing and
> Your turn alike — tearing it down only on kill or completion. So "working" =
> the TUI is mid-turn; `to review` = the same live TUI parked at its prompt;
> "send input" = **the user typing into that terminal**, which ACT hosts
> full-screen for the card.

**4. Your turn** *(machine-controlled → hands back to human)*
Everything the user is on the hook for, in one column; the badge says which kind:

- **needs permission** — a permission prompt is open in the TUI.
- **needs answer** — the agent asked a question.
- **error** — crash or non-zero exit.
- **killed** — the user terminated the session via the kill/abandon hatch.
- **to review** — the agent finished its turn cleanly (`Stop`, no question, no
  error). Work is produced and nothing is blocking.

The card is a *report* that the TUI is waiting — **ACT never answers on the
user's behalf** (see Hooks are observability, not control). In every case the
session is still alive and parked at its prompt (or, for error/killed, gone but
resumable), so the exits are the same regardless of badge:

- **→ Executing.** The user acts in the terminal — answers a prompt, or sends
  reviewed work back — and ACT sees the session move again (badge returns to
  `running`). For **error**, **Retry** does the same thing explicitly (see Error
  handling & retry).
- **→ Completed.** The user's explicit sign-off, available on any card in the
  column: a crashed or killed task is as legitimately done-with as a reviewed
  one. On the board it is a **drag onto Completed** — the same gesture as every
  other column change, so the strip carries no sign-off button; the session view
  keeps its **Complete** action for a card you are already inside.

Cards are ordered by cost of waiting — see the ordering note under Summary.

**5. Completed** *(terminal-ish, human-controlled)*
The user marks the task done from Your turn. Not strictly terminal: it has one
manual exit, **reopen → Your turn**, for when the user forgot some feedback.
From there the normal Your turn exits apply.

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

**Agent-emitted spawns** call the **`create_followup` MCP tool** — the whole
agent→ACT contract, described in *Agent ↔ ACT contract*. The file mechanism this
section used to specify (`.act/followups/`, ACT-prefixed filenames, atomic writes,
consume-on-ingest, a `FileSystemWatcher`) was **dropped on 2026-07-30**; the
reasoning is recorded there.

- **Arguments:** `{ title, prompt, cwd?, dependsOn? }` — `cwd` defaults to the
  parent's working directory.
- **The parent is the token, not an argument.** ACT resolves the calling session
  from the per-session token, so an agent can only spawn onto its own card.
- **ACT still owns id assignment.** Each child gets a freshly minted UUID and
  `number`, and the call **returns** them, which is what lets `dependsOn` name real
  ids instead of transport handles.
- **Ingested immediately, not at `Stop`.** The card lands in Ready when the tool is
  called; there is no scan trigger and no consumed directory, so ingestion is
  idempotent by construction — one call, one card.

`dependsOn` is the seed of task ordering (a plan implies sequence); ordering is
enforced by the scheduling runner — a task launches only after its `dependsOn`
prerequisites are Completed.

---

## Task / card data model

The card is the central entity (stored in LiteDB). Fields, grouped by concern:

### Identity — three distinct IDs

- `id` — ACT-minted **UUID**, permanent, internal. Used for store keys and
  `parentId` lineage. Never shown to the user.
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
  grants, follow-up instructions — are typed in the terminal and belong to the
  interaction history, **not** to this field.

### State

- `column` — `Preparing | Ready | Executing | YourTurn | Completed`.
- `badge` (execution status) — `running | needs-permission | needs-answer |
  error | killed | compacting | to-review`; null before launch. A name this build
  no longer knows (a card written before `stale` was retired) reads back as null
  rather than failing to load — see *Anything stored is a code*.

### Agent / session

- `agentType` — `claude-code | codex`.
- `sessionId` — see Identity.
- `workingDir` — the task's cwd.
- `launchConfig` — per-task launch parameters (see Launch config below).
- `schedule` — *when* the task may auto-launch: `manual | now | next-window |
  window-after-next | datetime`. Set at **creation** (new-task modal, default
  `manual`) and editable in Ready; only *displayed* as a badge in the Ready column
  (see Scheduling & queue policy).
- `autoGit` — optional, set at creation: the git actions
  (`commit` / `push` / `pr` + `draft`) the agent should do **in-session**, appended to the
  prompt as one sentence at launch. Independent of completion — the card still stops in
  Your turn for sign-off.

*(There is deliberately no `autoComplete` — see No auto-completion below.)*

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

- **No `lastMessage`.** Dropped on 2026-08-01 with the read-only blocked statement
  it was going to feed (see *Interaction*): it had two writers meaning different
  things — the rules engine writing what the user was being asked, the transcript
  writing what the agent last said — and never a single reader. Nothing on the
  board needs to repeat what is on the terminal's own screen.
- `observedModel` — the model the session **actually** ran (may differ from the
  requested `launchConfig.model` due to fallback or a mid-session `/model`
  switch). Stored alongside the requested one to avoid confusion.
- `metrics` — grouped health/at-a-glance object (derived/observed, not
  user-set):
  - `tokensIn` / `tokensOut` — cumulative input / output tokens over the whole
    task, counting **fresh work only**; `tokensTotal` derived. Reads off the
    prompt cache are deliberately excluded: a long session re-reads the same
    cached prefix on every request, and summing those reaches tens of millions
    and stops meaning anything.
  - `compactions` — counter, incremented on each `PreCompact` (thrash signal).
  - `contextUsed` / `contextLimit` — **live** active-context usage vs. the
    model's window (e.g. 142k / 1000k); drops after a compaction. Distinct from
    cumulative tokens in both directions: it is the *last* request's total,
    cache reads included, and it overwrites rather than accumulates. Percentage
    derived. The limit is **not observable** — no transcript reports a window —
    so it comes from `AgentModel.ContextLimit` in the adapter's capability list,
    matched against the *observed* model; an unknown model shows no percentage
    rather than a wrong one.
  - **No `cost`.** Dropped on 2026-07-30: nothing reports it, deriving it needs a
    price table that drifts silently as models and rates change, and on a
    subscription the number is notional. Tokens are shown instead — observed, not
    computed.
  - `turnCount` — number of exchanges.
  - `toolCalls` — number of tool invocations (activity/complexity signal).
  - `lastActivityAt` — timestamp of the last event; what the quiet chip is
    measured from.
  - `activeTime` — wall-clock time actually executing (optional; pairs with
    elapsed-since-`launchedAt`).

### Launch config

Set at Preparing time. ACT holds a **normalized** field set; **each agent
adapter maps it** to that agent's real CLI flags / settings, defines valid
values, and defines behavior for unsupported values (map to nearest equivalent
*or* reject at launch with a clear message — never silently drop).

**Two owners, joined at launch — decided 2026-08-01.** A *task* owns `workingDir`,
`model`, `effort` and `permissionMode`, because those describe the work. The
*machine* owns the **executable path, the extra flags and the environment**, because
those describe the install: where `codex.exe` lives does not change because the work
does, and a proxy variable one task needs, every task on this machine needs. They were
per-task fields under *Advanced* on the form, which meant retyping the same path on
every card and no way to fix it in one place when it moved — so they became **per-agent
settings** (*Settings → Agents*, one section per registered adapter). `LaunchComposition`
joins the two on the way to the adapter and is the only place they meet.

- **The machine half always wins, and is applied even when it is empty.** Cards written
  before the settings existed still carry their own copy, and reading one back would
  resurrect a path the user has since corrected — so the composition clears those fields
  first and no card's stale copy can reach a CLI.
- **Startup finds the CLIs so the common case needs no configuration** (`AgentInstallDiscovery`).
  The adapter owns *where to look* — that is agent knowledge — and the `IExecutableProbe` port owns
  the filesystem access, which keeps discovery testable without installing anything. Three
  outcomes, each doing something different: **on `PATH`** changes nothing, because an empty setting
  *means* "resolve by name" and keeps working when the install upgrades itself; **off `PATH` but
  installed** records the path, since a pty spawns with an explicit image and would otherwise fail
  for a CLI sitting right there; **not installed** leaves the path empty and, on the first pass
  only, switches the agent off in the task form. A path the user typed is never touched.
  - Candidates were measured, not guessed: Claude Code installs to `~/.local/bin` (and npm global),
    while Codex declares its own location as `CODEX_CLI_PATH` in `~/.codex/config.toml` — asked
    first, because it is the machine's answer rather than ACT's — then the Store layout under a
    build-hash folder, newest first.
- **The executable box is validated and browsable**, by the same rules the launch resolves with: a
  value with no separator is a name to look up on `PATH`, anything else is a path that must exist.
  It is a **warning, not a block** — settings apply as you type and a path you are about to install
  to is reasonable to enter.
  - **The empty case has three answers, not two, and conflating them was a real bug.** Empty means
    "resolve the bare name", so the only reassuring answer is that the name genuinely resolves.
    Asking the adapter whether the CLI was found *anywhere* reported "Found on PATH" for a Codex
    that is installed and **not** on `PATH` — praise for a box whose launch fails with "not found"
    for a binary sitting on the disk. So: on `PATH` → quiet confirmation; installed **off** `PATH` →
    a warning naming the path, with a **Use it** button that fills the box, because the fix is one
    click and retyping what ACT just found would be perverse; nothing found → "may not be
    installed". **Browse** opens the same picker the task
  form uses, in **file mode**: the walk is identical, files are listed alongside folders, and
  clicking one *is* the choice, so there is no confirm button. Its `FolderPicker` name went with the
  second mode — it is `PathPicker`, and `IWorkingDirectories.List` takes `includeFiles`, off by
  default because listing 40,000 files to choose a working directory would be felt.
- **Each agent has an `enabled` switch**, and the task form offers only the ones that are on — plus,
  always, the agent a card already carries, so turning one off never leaves an existing card facing
  an empty dropdown for a value it still holds. Discovery sets it once for an agent it cannot find;
  after that it is the user's, so re-enabling sticks.
- **A one-time migration lifts what a real board already had** (`AgentDefaultsMigration`,
  at startup, newest card wins, skipped once an agent has settings). Without it the loss
  is silent and specific: a Store-packaged Codex is on no `PATH`, so every existing Codex
  card would fail at spawn for a path the user had already supplied and could no longer
  see anywhere.

- `workingDir` — the task's cwd. **Stored as the user typed it and resolved at launch:** a
  leading `~` is expanded (nothing below a shell does it, so `~/dev/act` would otherwise
  reach `CreateProcess` verbatim and fail), separators are normalized, and the directory
  is checked to exist *before* spawning — a pty reports a missing one as "The directory
  name is invalid" tacked onto the entire command line, which buries the only fact that
  matters. The card keeps showing the short form. The **task form** runs the same check
  as you type and offers to create the directory, so the commonest launch failure is
  caught where it can still be fixed rather than at launch. Resolution is the
  `IWorkingDirectories` port, since the launch and the form need the same answer. Its placeholder names the two ways in ("Type a path, or
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
- `agentBinary` — path to the executable, **per agent in settings, not per task**.
  Resolved against `PATH` at launch, since a pty spawns with an explicit image path and
  does not search for one.
- `model` — per-task; **agent-specific** values (e.g. current Claude Code:
  Sonnet 5 / Opus 4.8 / Fable 5).
- `effort` — per-task reasoning effort; **agent + model-specific**. Not a flat list
  per agent: Codex's catalog gives each model its own ladder (`gpt-5.6-terra` reaches
  `ultra`, `gpt-5.6-luna` stops at `max`, `gpt-5.5` at `xhigh`), so capabilities are
  modelled as **models each carrying their own efforts and default**, and the
  new-task modal narrows the effort list when the model changes. Some models ignore
  effort entirely, so it may still be a no-op.
- `permissionMode` — **first-class normalized field**, fixed set ACT understands:
  `default | plan | acceptEdits | auto | dontAsk | bypass`. The *set* is shared and the
  stored value is normalized — so a card keeps its meaning if it is retargeted at the other
  agent — but **which of them a given agent offers is that adapter's capability**, and the
  form lists only those. Adapter-mapped
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
  | `dontAsk` | `-a never -s workspace-write` (failures returned to the model, never prompts) |
  | `bypass` | `--dangerously-bypass-approvals-and-sandbox` |

  **`auto` is absent from that table on purpose: Codex does not offer it.** It has no
  classifier tier, so the mode could only land on `acceptEdits`'s own flags — and
  `-a on-request` means *the model* decides when to ask, which is the opposite of the
  "never prompts" the mode promises. It was briefly offered anyway and substituted with a
  recorded adjustment; that only made sense while the form showed every mode to every
  agent. Since **the set of modes is a per-agent capability** — `AgentCapabilities.PermissionModes`,
  declared by each adapter in the order the form should show it, exactly as models are — Codex
  now offers five and Claude Code six, and a stored `auto` on a Codex card is *rejected* at
  launch with a message rather than quietly run as something else. Substituted or rejected,
  never both.

  - **Beware one name collision.** ACT's `plan` is the read-only/never-ask pair above. It is
    **not** Codex's *Plan collaboration mode* (`/plan` in the TUI), which makes the model propose
    a plan instead of doing the work and is what gates its `request_user_input` tool. Different
    axis, same word; see *Codex hook findings*.

  - **This is a state-machine knob, not just config.** It governs how often a card
    enters Your turn blocked: `default` ⇒ often; `acceptEdits` ⇒ less; `auto`,
    `dontAsk` and `bypass` ⇒ effectively never for permission, because none of
    them prompt. Note the difference that matters for unattended runs: `bypass`
    silently allows, while `dontAsk` silently **denies** — a `dontAsk` task never
    stalls overnight but may finish having been blocked from work it needed, so it
    trades a stalled card for a possibly incomplete one. `auto` sits between
    them, delegating the call to a classifier — on Claude Code only.
  - `plan` (read-only, no mutations) is the natural fit for a **planning task**
    that emits follow-ups without touching anything — pairs directly with the
    spawn/plan-decomposition pattern.
- ~~`allowedTools` / `disallowedTools`~~ — **removed 2026-08-01.** Claude Code mapped them
  to `--allowed-tools` / `--disallowed-tools`; **Codex ignored them entirely**, with no
  rejection and no adjustment — the one outcome the never-silently-drop rule above exists to
  forbid. Making the drop honest was the alternative; deleting them was chosen because
  `permissionMode` is the guardrail that works on both agents, and a half-honoured allow-list
  is worse than none. Anyone who wants Claude's flags back has `extraFlags`.
- `extraFlags` / `env` — escape hatch for anything not modeled. **Per agent, in settings,
  not per task** (see below).

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

**A source is a publisher, not a port.** Each one normalizes on its own side and
**pushes** into `IAgentEventSink`, which the session registry resolves to the live
session; the session hands the stream on to the rules engine. There is no
`IIngestionSource` interface to implement — that port was deleted on 2026-07-30 with
no implementations, because nothing about a hook post, a file change or a dying
process is something ACT pulls from.

**Source types:**

1. **HTTP hook** — agent POSTs event JSON straight to ACT's local endpoint
   (Claude Code `http` hooks). No shell/`curl`; cross-platform clean.
2. **Command-hook forwarder** — a command hook pipes stdin → ACT's endpoint
   (Codex, which supports command hooks only; also usable by Claude Code). A
   tiny shipped helper or `curl`.
3. **Transcript source** — tails the session's JSONL transcript for **enrichment
   only** (observed model, token counts, context %, last message) and, for an agent
   that cannot pre-mint its id, for the **session binding** and turn boundaries.
   Read-only; ACT never writes into a transcript. *(This replaces the former "file
   source". It no longer watches `.act/` for anything: the status file was dropped
   with auto-completion, and follow-ups moved to the MCP tool.)*
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
     the transcript leaves both null and the two never fight over a field. True of both
     agents since 2026-07-31 — Codex counted from its rollout for as long as its hooks
     were believed dead, and stopped when they were fixed.
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

### Hooks are observability, not control

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

#### Classifying a waiting prompt (measured 2026-07-30, `claude-code v2.1.220`)

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

#### The one prompt no hook reports (measured 2026-07-30, `claude-code v2.1.220`)

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

### Edge cases

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
  is gone (killed, exited on its own, signed off, or a restart that could not reach
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
  - A kill still kills. The restore happens on the *next* open or the next start,
    never in reaction to the kill itself.

---

## Rules engine

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
| Executing | killed by the user | Your turn | `killed` |
| Executing | startup prompt waiting *(no hook at all since the spawn)* | Your turn | `needs permission` |
| Your turn | activity observed *(the user answered, or sent it back, in the terminal)* | Executing | `running` |
| Your turn + `needs answer` | permission requested | *(ignored)* | *(stays)* `needs answer` |
| Your turn | startup prompt waiting | *(ignored)* | *(stays)* |

### There is no completion signal — dropped 2026-07-30

A `Stop` is a `Stop`: **every turn end lands in Your turn with `to review`**, and
ACT does not try to tell a finished turn from one that ended on a question.

The spec used to specify a status file (`.act/status/<taskId>-<turn>.json`, `{ state:
"ready_for_review" | "needs_input" }`) written before every turn end, so the badge
could distinguish the two. It was designed before `Stop` was measured, when it was
also the only thing that could route Executing → Your turn at all. Three things then
removed its reason to exist:

- **`Stop` does the routing.** Measured and verified live at step 9 — the card walks
  to Your turn on the hook alone.
- **The badge distinction stopped mattering.** Needs feedback and To review merged
  into one column (see *Why one column and not two*), so both outcomes were already
  targeting the same place and differing only in a label.
- **The two blocked cases are observed, not self-reported, and with better fidelity.**
  A permission gate arrives as `Notification`/`permission_prompt`; a question arrives
  as `PreToolUse`/`AskUserQuestion` carrying the actual question text — which no
  status file ever had. The only case left uncovered is an agent that ends a turn
  asking something in *prose*, and that costs the user one click to discover.
- **Its last real consumer went away.** Auto-completion needed a positive "done,
  nothing blocking" assertion, because `Stop` alone cannot support skipping review.
  With no auto-completion (see *No auto-completion*), nothing in ACT needs the agent
  to assert anything.

What that buys: no `.act/` directory in the user's repository, no watcher, no
atomic-write convention, no `<turn>` counter for the agent to track, no advisory rule
it can silently fail — and an injected preamble that shrinks to nothing (see *Agent ↔
ACT contract*).

### No stale badge — the quiet chip instead

*(Decided 2026-07-31, reversing the `stale` badge this spec carried from the start.)*

**The question that killed it:** every block on the user already moves the card. A
permission prompt, a question, the pre-session trust screen — each has a signal, and
each lands in Your turn. So what is left that sits in Executing and is genuinely
hung? Not the model: measured across 2,729 within-turn gaps in real Claude Code
transcripts, p50 is 1.3 s, p99 is 45 s, and **every gap past three minutes turned out
to be a session waiting on its human**, not working. An agent that stops answering is
not a thing this codebase has evidence for.

What silence in Executing actually means, honestly enumerated:

- ~~**Codex, entirely**~~ — **no longer true, and it was the lead justification.** This said
  Codex's hooks do not fire, so silence was the only signal such a card could give. They do
  fire (ACT was quoting the hook command); a Codex card now reports a waiting prompt through
  `PermissionRequest` like any other. The chip keeps the reasons below, which never depended
  on it.
- **A Codex card whose hooks the user declined to trust** — answering the hook-review screen
  with *"Continue without trusting"* means no payloads at all, and nothing else names the
  rollout either, so such a card really can only be read through silence.
- **Blocked *inside* a tool call** — permission was already granted and the command
  itself is waiting (an interactive CLI reading stdin, an install stuck on a private
  registry). The agent is not asking, so no hook fires.
- **A rate limit hit mid-turn** — the CLI sits showing a reset time. `waiting-reset`
  is a *Ready*-column state for the queue runner; nothing covers this.
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

### Other resolutions

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

### Error handling & retry

Any `error` in Your turn is **manually retriable** — a **Retry** action in
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
- **In-session git failure** (`autoGit`) — not a distinct case: the agent's turn ends,
  the card lands in Your turn like any other, and the user sees the failure on review.
  ACT does not detect it, because nothing reports it.

So the drawer's action set is state-specific — and, since ACT no longer answers
anything, mostly a way *into* the terminal: permission / question → **Open
terminal**; **error → Retry (± edit launch config)**; review → complete / kill, or
Open terminal to tell it what to do next. Open terminal, Open in Desktop and Kill
are available on any active card.

### No auto-completion — decided 2026-07-30

**Every finished turn stops in Your turn, and only the user's drag reaches
Completed.** There is no `autoComplete`, and no card ever signs itself off.

- **Completion is the one thing ACT asks of the user, and it is the whole point of
  the board.** A tower that closes its own cards is a log, not a control tower —
  the value is that a finished run *waits* for you to look at it. Making the
  sign-off skippable makes the review optional, and an optional review is the
  first thing to get skipped on the night it mattered.
- **It removes ACT's only would-be dependency on the agent's self-report.**
  Auto-completion was the sole consumer that needed a positive "I am done and
  nothing is blocking me" assertion from the agent, because `Stop` alone cannot
  distinguish finished work from a turn that ended on a question asked in prose.
  Without it, the observed events carry the whole model — see the *Agent ↔ ACT
  contract*.
- **Control arc stays human → machine → human**, with no opt-in that drops the
  final step.

**`autoGit` survived, re-based on the prompt.** Its old trigger was "before the
`ready_for_review` file on an auto-completing task", which no longer exists — so it was
re-cut as a **prompt suffix** instead, independent of completion: pick a git action on the
task form and ACT appends one sentence to the prompt at launch
(`Once you are done: commit, push and create a draft pull request.`). The agent does the
work in-session, the card still stops in Your turn for sign-off, and a failed git step is
simply something the user sees on review. Git chosen at *review* time remains separate —
that is the spawned task in *Git handoff on completion*.

### Agent ↔ ACT contract — one MCP tool, decided 2026-07-30

**The agent→ACT contract is a single MCP tool, `create_followup`, and nothing else.**
There is no status file, no follow-up file, and no `.act/` directory. Everything ACT
knows about a running session it *observes* (hooks, transcript, process); the one
thing an agent can *tell* ACT is that some work belongs in its own task.

| | `mcp__act__create_followup` |
|---|---|
| Transport | streamable HTTP on ACT's kept hook port, `/mcp` |
| Auth | the session's existing hook token, `x-act-hook-token` |
| Arguments | `{ "title", "prompt", "cwd"?, "dependsOn"? }` |
| Returns | the minted card's number and id |
| Not called | nothing spawned |

**Why a tool and not a file or a curl command.** All three cost about the same in
tokens — 150–250 in a prompt-cached prefix — so the deciding factors were elsewhere:

- **The payload is the wrong shape for a shell command.** `prompt` is long free text
  with quotes and newlines in it; getting that through a `curl -d` body and whatever
  shell the agent's Bash tool got is the same class of bug that truncated the launch
  argument (see *Launch*). A typed schema removes the quoting question entirely.
- **The grant is narrower.** One pre-allowed tool id (`mcp__act__create_followup`)
  versus allowing the agent all shell `curl`, or all writes under a directory.
- **It very likely sidesteps both sandboxes.** The MCP connection is made by the CLI's
  own process, not by a sandboxed tool invocation, so Codex's `workspace-write`
  network block and Claude's bash sandbox should never enter the picture.
  ⚠️ To verify at step 12.
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

**The preamble shrinks to nothing, and stays that way.** With the status convention
gone, `AgentPreamble` has no remaining job: the injected instruction block goes away
and the opening prompt is the user's task text alone. `ActContract` and
`AgentPreamble` are deleted with it, along with the largest and most quote-dense part
of the launch argument.

**One exception, and it proves the rule: `autoGit`.** When the task carries a git
action, `AutoGitInstruction` appends a single sentence to the prompt at launch. That is
not ACT teaching the agent an ACT convention — it is ACT phrasing *the user's own*
instruction, chosen on the task form, which they would otherwise have typed into the
prompt themselves. It is composed at launch and never stored, so `Card.InitialPrompt`
stays verbatim for the life of the task as the form promises.

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
- **If broader framing is ever wanted**, MCP's own `instructions` field in the
  initialize response is where it belongs — it travels over the connection, not in
  the opening prompt. ⚠️ Whether either CLI surfaces server instructions to the model
  is unmeasured; see step 12.
- **It keeps the read-only stance total.** ACT never parses the screen, never answers
  a prompt, and every source reports rather than commands. An injected instruction
  block is the write-side version of exactly that — dropping it makes the principle
  complete instead of nearly so.

One candidate for a future one-liner, unrelated to MCP and deliberately deferred:
since nothing auto-completes, the agent's closing message is what the user reads on
the card when deciding to sign off. Telling the agent that might improve it — but
leaving it out is the only way to learn whether it needs saying.

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
Executing and Your turn alike — until the user kills it or the
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

The pre-session trust prompt is the one state no hook can report, and it does not
bend this rule: what ACT observes there is its own process staying silent, never a
string on the screen. See *The one prompt no hook reports*.

### The input channel is the human at the keyboard

**ACT types nothing into a session at all.** Every byte reaching a live agent is a
keystroke the user made in the terminal ACT is showing them; ACT's only writes to
the pty are the resize and the kill.

*(Until 2026-08-01 there was one exception — a **send-back message**, seeded from
the UI into a session parked at its prompt, going in as bracketed paste followed by
the agent's submit key. It is cut. Send-back only ever meant "reply to a finished
card without opening its terminal", and in an app where the terminal is a tab on the
card, that is a worse text box a click away from a better one that is already open.
It was also the last thing keeping ACT in the business of guessing when a TUI is
ready to be typed into — a guess step 7 paid for once. `IAgentTerminal.SubmitAsync`,
`TerminalSubmitProfile` and the paint/settle waits behind them are deleted, and the
rule is now absolute rather than "exactly one thing".)*

The **initial prompt is not typed**: it is a positional argument on the launch
command line for both agents (and a resume message likewise). Typing it looked
equivalent and is not — measured at step 7, a CLI that has painted its banner is
not yet listening to its prompt line, so the prompt landed nowhere and the card sat
at an empty composer while the board said it was executing. On the command line it
is in place before the TUI paints, and there is no race to lose.

Everything else — answering a permission prompt, answering a question, approving a
plan, replying to finished work, `/`-commands — the user types themselves, in the
terminal ACT is showing them.

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

- `maxConcurrent` — cap on tasks past the launch boundary that are not yet
  awaiting sign-off (i.e. **Executing**, plus **Your turn** on any badge but
  `to review` — blocked-but-alive sessions count).
- **Rate-limit backpressure (safety net)** — if an eligible task's usage window
  is exhausted, it **waits for reset** (shown as `waiting-reset` in Ready)
  rather than erroring. Distinguish the two limits: 5-hour window → wait hours;
  weekly cap → wait days (don't retry hourly against a weekly lockout). So
  `schedule` is *intent*; cap + backpressure are *reality*.

### Spawned-task schedule defaults

- **ACT-emitted** (git commit/push/PR): default **Now**; user can override the
  schedule right in the Your turn → Completed git modal.
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

- ✅ **"Next window" needs the 5-hour reset time — solved.** Neither computing it
  from tracked activity nor reading `/usage` is necessary: both vendors serve the
  reset instant over HTTP (see *Usage indicator* below), so `schedule` reads the real
  boundary rather than estimating one.
- ✅ **Weekly reset** — likewise served, so it needs no per-account configuration.
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
- Verify at build: clean mid-task resume after a rate-limit interruption.

### Usage indicator

The top bar carries a live meter per usage window per agent — percentage used, how
long until it resets, and the local time it resets at. It is the same data
`schedule` and backpressure reason about, made visible.

**Source: a live HTTP endpoint per agent**, authenticated with a bearer token read
from that CLI's own credential file, polled once a minute. Endpoints, response
shapes and the alternatives that were rejected (Claude Code's `statusLine` payload,
scraping `/usage` from a pty, deriving from transcripts, Codex's rollout
`rate_limits`) are recorded in **`docs/agent-usage-findings.md`**. Read it before
touching usage code.

Three rules the design turns on:

- **A window is named by the length the server declares.** A paid Codex plan reports
  5-hour plus weekly; a free one reports a single 30-day window. Two hardcoded
  captions would mislabel a real account.
- **A reading held past its own reset reports zero, not the number it was given.**
  ACT polls, so nothing ran in between; the old percentage describes a window that no
  longer exists. Rendered subdued, because it is inferred from this machine alone —
  usage from another machine, the web, or an IDE extension counts against the same
  quota and is invisible here.
- **ACT reads those credential files and never writes them.** Each CLI owns and
  refreshes its own; ACT re-reads before each poll and never attempts a refresh,
  because refresh tokens rotate and racing the CLI could invalidate the user's login.
  The token is never logged, never persisted, and goes nowhere but the vendor.

Everything degrades to *usage unavailable*: a missing file, an expired token, a 401,
a timeout, a changed response shape. Nothing about the indicator can break the board.

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
  run, where and how — title, prompt, working directory, agent, launch config and
  schedule. The copy is titled **"Copy of &lt;title&gt;"** (localised), so two cards that
  differ only by number never look identical on the board. It lands in **Preparing** with a
  fresh id and number, and with no session
  id, badge, metrics, observed model, last message or transition history: a copy is a task
  that has not started, not a fork of a session. **Lineage is dropped** — the follow-ups a
  card spawned belong to the run that spawned them, so a copy has neither children nor a
  parent. Duplicating an archived card leaves it archived; the copy is live, which is the
  point — it is how a finished or abandoned task gets run again without disturbing its record.
- **Artifact housekeeping:** ACT writes nothing into the user's working directory, so
  there is nothing there to clean up — the generated launch config (settings / profile /
  MCP config) lives in ACT's own data directory and is removed when the session ends. It
  **never** touches the agent's transcripts (not ACT's to delete, and needed for resume).

### Session lifecycle — resumability rides the transcript

Resumability does **not** depend on ACT keeping anything alive. Retry
(Your turn → Executing) and reopen (Completed → Your turn) work via `--resume
<sessionId>` against the agent's **on-disk transcript**, which persists
independently of ACT.

- Per the liveness model, ACT keeps the terminal alive for the whole active life
  of a card and tears it down on kill or completion. Opening a **Completed** card's
  terminal, or any card whose terminal died with ACT, re-spawns via `--resume` into a
  fresh terminal. That re-spawn is **automatic** on open for every bound card that has
  been launched, Completed included, and additionally at startup for the machine
  region (see *Edge cases*). What Completed still waits for is the **reopen** — the
  move back to Your turn — not the terminal.
- **Caveat:** the transcript is outside ACT's control, subject to the agent's
  retention or user deletion — so a very old Completed task may no longer be
  resumable.
- **Fallback:** reopen/retry attempts `--resume`; **on failure, offer to
  start a fresh session seeded with the stored `initialPrompt`** rather than
  failing silently. Record which path was taken.

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

Optional git handoff on the **Your turn → Completed** transition. The completion
modal prompts for an optional action: **commit**, **push**, **create PR** (with a
**draft** checkbox). Actions chain (commit → push → PR; draft modifies the PR
only); "no git action" is always available.

- **Mechanism:** the completing task simply **completes**, and ACT **spawns a new
  git task in Ready** (ACT-emitted — ACT writes its prompt from the user's
  choice) to do the commit/push/PR. No Executing round-trip on the parent; the
  git work is its own tracked card that can land in Your turn on its own — just
  the general task-spawning mechanism applied to git. The completion modal also
  offers the spawned task's `schedule` (default **Now**).
- **Contrast — `autoGit` tasks.** When git is known at **creation**, it rides the prompt
  as a suffix and the agent does it in-session, so there is no spawned card. Spawning
  applies to git chosen at **review time**, which was not known at launch.

---

## Native OS notifications

Load-bearing, not cosmetic: when you're not watching the board (especially
overnight), the OS notification **is** the "needs you" signal. Without it,
unattended mode is half-blind.

### Events → triggers (map to attention-state transitions)

- **Blocked in Your turn** — `needs permission`, `needs answer`, `error`,
  `killed` (blocked on you). Always on.
- **`to review` in Your turn** — a task finished and wants sign-off. This *is* the
  overnight "it's done" ping, since nothing completes itself.
- **`waiting-reset` / rate-limited** — queue paused until the window resets, and
  again when it resumes.

There is nothing to notify for a *quiet* card: silence is not a state and ACT makes
no claim about it (see *No stale badge*). Waking someone for a card that may simply
be running a long build is exactly the false alarm the chip exists to avoid.

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
  blue=idle/to-review, muted green=done, dim yellow=quiet. Brand mark = a small
  radar sweep.
- **Launch is "Launch now".** Named for what it will mean rather than what it does
  today: once the scheduler lands (step 14) a card can be waiting on a `schedule`, and
  this button is the **override** that starts it regardless. Naming it plain "Launch"
  now would have to change then, and the two would read as different actions.
- **Board:** **five flat columns — no persistent zones.** The launch-boundary
  rule is shown **dynamically at drag time**: picking up a draggable card lights
  only its valid drop targets and **grays out invalid columns** (Preparing ↔
  Ready; Your turn → Completed only). An **Executing** card does not lift at all —
  it is the one column with a live turn to protect.
- **Column lanes:** each column is a **bay** — a faint full-height track
  (`--act-lane` fill, `--act-lane-line` hairline, rounded) that separates the
  columns and, crucially, keeps an **empty** column legible instead of collapsing
  to a floating header. An empty bay shows one muted mono line (`no cards`).
  Deliberately **not** a dashed drop-zone: most columns take no drop at all, so a
  standing drop affordance would teach the wrong rule — the drag-time lighting is
  what says where a card may land.
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
  + pulse + `!` corner); no separate inbox, and no top-bar counter either. A
  **"N need you ›"** pill was there and is gone: ACT answers no permission prompt,
  so the pill could only ever count cards and point at the board it sat above.
  The card is the signal — the strip is already loud, and the columns already
  group by state.
- **Interaction — two surfaces, one rule.** The **drawer** is where you *read* a
  card; the **session view** is where you *talk* to it.
  - **Contextual right-side drawer** over a dimmed board, action set by state.
    Actions: Open terminal, Open in Desktop, Kill, **Retry** (± edit launch config,
    on `error`), Complete. Open terminal / Open in Desktop / Kill on any active card.
    - **No Send back — decided 2026-08-01.** Replying to finished work is typing into
      the session, and the session is a tab on the card. A compose box in the drawer or
      the rail is a worse text input inches from a better one, and it was the last thing
      ACT would have typed on the user's behalf; see *The input channel is the human at
      the keyboard*, which is now absolute.
    - **The badge is the whole report — decided 2026-08-01.** An earlier design had a
      blocked card carry a brief read-only statement of what was being asked, fed by
      a `lastMessage` field. It is cut, and the field with it. `needs permission` /
      `needs answer` already says the one thing the board is for — that this card
      wants you — and the answer is typed one click away, on the screen the question
      is actually rendered on. Repeating a truncated copy of it on a flight strip
      costs the space the strip does not have, and buys a second place for the same
      sentence to go stale in: nothing reports a permission being *approved*, so the
      copy would outlive the prompt (the same staleness the `needs permission` badge
      already accepts, but in prose and far more conspicuous).
  - **Session view — a full-screen route per card.** The xterm terminal takes the
    window, with a right rail carrying identity (`#1042`, cwd, agent/model) and the
    live metrics (context %, cost, turns), the same action set, and back-to-board in
    the top bar. This is the answer to every "needs you" state — it is where the card
    and the OS notification both lead.
  - The terminal replays its scrollback on entry, so leaving the view and returning
    is free.
  - **Transient failures are notifications, not page furniture.** A failed launch is a
    path or a command line — it does not fit a 15rem rail, and it is news rather than a
    property of the card. It goes to the notification host; what the *card* carries is
    the `error` badge and the transition note, which persist. Anything that must survive
    a dismissal belongs on the card, not in a toast.
- **New task / edit task:** a **page**, not a modal — `/card/new` and
  `/card/{id}/edit`, matching the session view. The control set is what the *task* owns —
  title, prompt, dir, agent, model, effort, permission mode, schedule, git-when-done — and
  a **single Save** → lands in Preparing; the user drags it onward. The fields sit in a
  centred column so a wide window does not stretch them.
  - **There is no *Advanced* section any more.** It held five fields: the tool allow/deny
    pair, deleted for being silently dropped by one of the two agents, and the executable /
    flags / environment, which moved to *Settings → Agents* because they describe the
    install rather than the work. Nothing was left to collapse.
  - **Past the launch boundary the form gates itself** (`TaskEditing`): prompt, dir, agent,
    schedule and git-when-done freeze — they describe the run that already started — while
    the title and the launch config stay editable, since those are what the *next* launch
    uses. Enforced in the code that writes the card, not only in the markup, so the
    immutable-`initialPrompt` rule holds however the form is rendered. One notice at the
    top says why, once.
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
    **Executing**; anywhere else the page says to move the card to Ready. Otherwise
    the toggle would quietly become a way to skip Ready and break the control arc.
    A card that already has a `sessionId` never needs the button: opening its terminal
    resumes the session by itself (see *Session lifecycle*), which is a re-attach to work
    that was already started rather than a launch.
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
  - **The window controls sit on ACT's own bar, not on a strip of their own.** Under
    Electron the frame is hidden and the native minimise/maximise/close are drawn as a
    Windows overlay on top of the page. Its background is left **transparent** so the
    top bar's gradient and the rule under it run edge to edge: a fixed colour there can
    only be set once, at window creation, so it cannot follow the theme and it painted a
    flat block over the end of a bar that is neither flat nor unruled. Only the glyph
    colour stays native — one grey chosen to read on both themes.
- **Settings page** (`/settings`): the home for scattered preferences, opened from the
  gear in the top bar and grouped by type (**General**, **Appearance**, more to come).
  Every setting applies immediately and persists to LiteDB — no OK/Cancel, and no Close
  either: the only action is the back arrow. A page rather than a dialog for the same
  reason as the task form — see *Interaction*. It also fixes a wart the dialog had: a
  language change forces a reload, which used to dismiss the dialog as a side effect and
  dump you on the board; now you stay on settings.
  Shipped: **language**, **theme** (follow-OS / light / dark override),
  **display mode** (compact / spacious — settings-only; there is no top-bar
  density toggle), **blink in Your turn**, **keep-awake**, **close-to-tray**,
  **Agents** — one section per registered adapter carrying that CLI's **enabled** switch,
  executable, extra flags and environment (see *Launch config*; these are properties of
  the install, which is why they are here and not on the task form) — and
  **New task defaults**, the template a new card starts from. Still to land:
  - **New task defaults** covers everything the task form asks for **except the title and the
    prompt**: working directory, agent, model, effort, permission mode, schedule and
    git-when-done. Those two are the task itself, and pre-filling them would mean creating the
    same task twice by accident. Changing the default agent clears a model or effort the new one
    does not offer, and moves the permission mode to one it does, rather than storing a default
    that would be rejected at launch.
  the notification matrix, `maxConcurrent`, weekly-reset time, and the auto-archive window /
  auto-execution pause.
  - **Blink cards in Your turn** (*Appearance*, on by default) governs the attention pulse only.
    Off keeps the rail colour, the glow and the border — the card still reads as needing you, it
    simply holds still, which is exactly what a reduced-motion user already gets. The state is
    never what gets turned off, because a card waiting on the user must stay legible as one.
  - **Keep the computer awake** (*System*, on by default) holds the machine up for as
    long as ACT runs — not per session, because a queue that opens at 02:00 needs the
    machine already awake rather than woken by work it cannot start. It is `ISleepInhibitor`,
    a port, because no two platforms spell it the same way: Windows takes a flag on a thread
    and drops it when that thread ends (so one parks for the life of the hold), while macOS
    and Linux express it as a child process that must stay alive — `caffeinate` and
    `systemd-inhibit`. Every path degrades to doing nothing rather than throwing. It is
    **on by default and applied at startup**, so it survives a restart instead of quietly
    resetting.
    This is what the old **"☕ awake"** chip in the top bar claimed to report; it never
    reported anything, so it is gone.
  - **Close to the notification area** (*System*, desktop-only, on by default) makes the
    window's close button leave ACT running behind a tray icon. The window is genuinely
    **destroyed and rebuilt**, not hidden: Electron.NET's `close` handler never calls
    `preventDefault`, so a close cannot be cancelled from C#. That costs a page load on the
    way back and nothing else — sessions belong to the registry, not to a view, so agents
    keep working across the gap and the terminal re-attaches. The switch is hidden in browser
    mode, where there is no window to close and no tray to close it into.
    - **Exit lives on the tray icon, behind a confirmation** that names what is lost: the
      running tasks it will stop (with a count, when there are any) and the scheduled tasks
      that will not run while ACT is closed. It is a **native message box**, because the
      window it would otherwise open in is usually the one just closed. Electron shows
      nothing for a parentless message box, so choosing Exit with no window brings the window
      back to carry the question — being shown what is about to stop is no bad thing.
      Confirming ends every live session first: the confirmation promised it, and an agent
      must not outlive the app supervising it.
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
