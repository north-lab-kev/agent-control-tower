# Task / card data model

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

The card is the central entity (stored in LiteDB). Fields, grouped by concern:

## Identity — three distinct IDs

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
- `title` — the task's name. **Optional to type**, and optional to *hold* only while title
  generation is switched off — see *Auto-generated titles* below. Editable for the life of the
  card, unlike the prompt, because a name is not part of the run.
- `initialPrompt` — the **immutable** opening instruction, set in Preparing.
  Preserved verbatim across all later turns (enables re-run-from-scratch and
  auditing what was originally asked). Subsequent inputs — answers, permission
  grants, follow-up instructions — are typed in the terminal and belong to the
  interaction history, **not** to this field.
- `attachments[]` — the files handed to the task alongside its prompt. **Names only**,
  never paths: the bytes live in ACT's own data directory under the card's `id`, and
  that root moves with `ACT_DATA_DIR`. See *Attachments* below.

## State

- `column` — `Preparing | Ready | Executing | YourTurn | Completed`.
- `badge` (execution status) — `running | needs-permission | needs-answer |
  error | killed | compacting | to-review`; null before launch. A name this build
  no longer knows (a card written before `stale` was retired) reads back as null
  rather than failing to load — see *Anything stored is a code*.

## Agent / session

- `agentType` — `claude-code | codex`.
- `sessionId` — see Identity.
- `workingDir` — the task's cwd.
- `launchConfig` — per-task launch parameters (see Launch config below).
- `schedule` — *when* the task may auto-launch: `manual | now | next-window |
  datetime`. Set at **creation** (new-task modal, default
  `manual`) and editable in Ready; only *displayed* as a badge in the Ready column
  (see Scheduling & queue policy).
  - **There is deliberately no `window-after-next`.** "Start two resets from now"
    is a duration dressed as a boundary: the thing a user actually wants that far
    out is a time, and `datetime` already says it exactly. A stored
    `WindowAfterNext` from an older build reads back as `manual`.
- `scheduledFor` — the datetime the user picked, for `datetime` only.
- `eligibleAt` — the instant a *relative* schedule was resolved to when the card
  was armed, for `next-window` only. Written by the queue
  runner, cleared when the schedule changes and when the card launches; see
  *Runner logic* for why it is stored rather than recomputed.
- `allowConcurrentWorkingDir` — the card's exemption from the one-task-per-folder
  guard (see *One task per working directory*). Default false; only meaningful
  while the global `preventConcurrentWorkingDir` setting is on, which is why the
  form hides it otherwise.
- `autoGit` — optional, set at creation: the git actions
  (`commit` / `push` / `pr` + `draft`) the agent should do **in-session**, appended to the
  prompt as one sentence at launch. Independent of completion — the card still stops in
  Your turn for sign-off.

*(There is deliberately no `autoComplete` — see No auto-completion below.)*

## Lineage

- `origin` — `manual | spawned`.
- `parentId` — nullable (set on spawned tasks).
- `children[]` — **stored (both directions)** alongside each child's `parentId`.
  Chosen over a derived list to preserve **explicit child order** (meaningful for
  a plan that spawns a sequence; ties into `dependsOn`).
  - *Consistency rule:* create / delete / re-parent must update **both sides in
    one operation**, via a single centralized link/unlink routine — never ad-hoc
    — so the two copies can't drift.
  - `children[]` is also the **spawn budget**: `create_followup` refuses once it holds
    the configured cap, so the limit is per card for its lifetime rather than per
    session, and a relaunch cannot refill it.
- `spawnAuthor` — `act | agent` (when spawned; affects labeling).
- `dependsOn` — ordering seed on spawned tasks (enforced by the scheduling
  runner: a task launches only after its `dependsOn` are Completed). Any card may be
  named, not only a sibling.

## Timing & history

- `createdAt`, `launchedAt`, `completedAt`.
- `transitions[]` — a per-task **log**; each entry records `at` (timestamp) and
  what changed (`column` and/or `badge`, optional `note`). Powers time-in-state,
  audit trail, and a per-card timeline in the UI.

## Enrichment & metrics (read-only, observed — kept fresh from the JSONL/file source)

- **No `lastMessage`.** It would have two writers meaning different things — the
  rules engine writing what the user was being asked, the transcript writing what
  the agent last said — and no reader: nothing on the board needs to repeat what
  is on the terminal's own screen.
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
  - **No `cost`.** Nothing reports it, deriving it needs a price table that
    drifts silently as models and rates change, and on a subscription the number
    is notional. Tokens are shown instead — observed, not computed.
  - `turnCount` — number of exchanges.
  - `toolCalls` — number of tool invocations (activity/complexity signal).
  - `lastActivityAt` — timestamp of the last event; what the quiet chip is
    measured from.
  - `activeTime` — wall-clock time actually executing (optional; pairs with
    elapsed-since-`launchedAt`).

## Launch config

Set at Preparing time. ACT holds a **normalized** field set; **each agent
adapter maps it** to that agent's real CLI flags / settings, defines valid
values, and defines behavior for unsupported values (map to nearest equivalent
*or* reject at launch with a clear message — never silently drop).

**Two owners, joined at launch.** A *task* owns `workingDir`, `model`, `effort` and
`permissionMode`, because those describe the work. The *machine* owns the
**executable path, the extra flags and the environment**, because those describe the
install: where `codex.exe` lives does not change because the work does, and a proxy
variable one task needs, every task on this machine needs — so those are **per-agent
settings** (*Settings → Agents*, one section per registered adapter), fixed in one
place rather than retyped per card. `LaunchComposition` joins the two on the way to
the adapter and is the only place they meet.

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
  clicking one *is* the choice, so there is no confirm button. The picker is `PathPicker`, and
  `IWorkingDirectories.List` takes `includeFiles`, off by default because listing 40,000 files to
  choose a working directory would be felt.
- **Each agent has an `enabled` switch**, and the task form offers only the ones that are on — plus,
  always, the agent a card already carries, so turning one off never leaves an existing card facing
  an empty dropdown for a value it still holds. Discovery sets it once for an agent it cannot find;
  after that it is the user's, so re-enabling sticks.
  - **The switch also takes that agent off the usage indicator, and stops polling for it.** A quota
    you cannot spend is not a number to act on, and the top bar is the one surface with no room for
    information that leads nowhere. The pump checks the switch each tick rather than at startup, so
    it stops and resumes without a restart.
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
  - **A create opens on the last folder a created task named** whenever the template it
    starts from leaves the folder blank; a template that names one wins. The commonest
    sequence of creates is several tasks against the same repository, so the folder — unlike
    the title and the prompt, which the default template is forbidden to carry precisely so
    tasks do not begin as copies of each other — is worth carrying forward. Stored in the
    settings document (`lastWorkingDir`), written by every create that names a folder, and
    deliberately **not a setting the UI offers**: it is a memory, not a choice, and a create
    without a folder does not erase it.
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
  "never prompts" the mode promises. **The set of modes is a per-agent capability** —
  `AgentCapabilities.PermissionModes`, declared by each adapter in the order the form should
  show it, exactly as models are — so Codex offers five and Claude Code six, and a stored
  `auto` on a Codex card is *rejected* at launch with a message rather than quietly run as
  something else. Substituted or rejected, never both.

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
- **There are deliberately no `allowedTools` / `disallowedTools` fields.** Codex has no
  equivalent and would drop them silently — the one outcome the never-silently-drop rule above
  exists to forbid — and a half-honoured allow-list is worse than none. `permissionMode` is the
  guardrail that works on both agents; Claude's tool flags can ride `extraFlags`.
- `extraFlags` / `env` — escape hatch for anything not modeled. **Per agent, in settings,
  not per task** (see below).

*Precedence caveat:* ACT's per-task flags layer **on top of** the project's and
user's existing `settings.json` (CLI flags > project > user > built-in
defaults). A project deny list still applies underneath — ACT does **not** fully
own permissions.

## Task templates

A **template** is a saved starting point for a new task: everything the task form asks
for — named, so a board that hosts several kinds of work can keep one per kind.

- **A named template carries the prompt and the title; the default carries neither.**
  Pre-filling either on the default means creating the same task twice by accident — but on a
  template you ask for by name they are most of the value. "Bugfix on repo X, Codex,
  accept-edits" is a prompt skeleton with a launch config attached, not a launch config with a
  prompt bolted on. So the split is by *which* template: both fields are optional on a named
  one and absent from the default.
- **The title is templated too, and optional.** A title names *one* task, so a template
  carrying one puts the same name on every card it makes. That is a reason to make it
  **optional**, not to refuse it: a recurring task genuinely wants a fixed name ("Nightly dependency sweep"), and
  the field is where a user would look for it. So a template's `title` is copied into the
  form's title box, which makes it a *typed* title as far as the save is concerned and wins
  over the prompt — and a template that leaves it blank falls straight through to whatever an
  untitled save does, generated or derived (see *Auto-generated titles*). One
  consequence worth having: a template with a title costs **no CLI call** to create a task
  from, because there is nothing left to derive.
  - **The template's `name` and the task's `title` are different fields, and both stay.** The
    name is how the template reads in the picker and the list — and what the list sorts on; the
    title is what the cards are called. *Save as a template* asks for the name in its dialog and
    suggests the task's title for it, and separately carries that title into the template, so the
    two can coincide on the way in and be pulled apart afterwards. Merging them was considered
    and dropped: they diverge the moment a template called *Nightly sweep* should produce cards
    named *Nightly dependency sweep*, or a template deliberately leaves the title blank so each
    card is named from its own prompt.
- **`schedule`'s specific datetime is the one thing dropped.** A moment is not a habit, and a
  template holding one would arm every task it created for a time that has already passed. The
  other three schedules (`manual` / `now` / `next-window`) are relative and template fine, and
  the template editor simply does not offer the fourth.
- **Exactly one template is the default, and it is a restricted one.** It is what the plain
  **New task** button uses, so a store without one is a board whose New-task button has nothing
  to start from — `UserSettingsService` seeds it on the way in rather than leaving it to the
  pages that rely on it, because a fresh install has no settings document at all for it to be
  read from. Three things about it are not the user's:
  - **It cannot be deleted.**
  - **It cannot be renamed, and its name is not stored.** It reads as *Default* / *Par défaut*
    from the resources (`TaskLabels.Template`), so it follows the UI language instead of freezing
    whichever one the install first ran in — *anything stored is a code* applied to the one
    template name ACT writes rather than the user. Every *other* template requires a name, so
    that is the only case with nothing stored to show.
  - **It carries no title and no prompt.** Those are the task itself, and pre-filling either on
    the template the bare `+` starts from means every task begins as a copy of the last — which
    is exactly why the settings block this replaced carried neither. A *named* template is asked
    for by name, so it may carry both.
  All three are enforced in `SaveTemplate` (and re-applied on load, so a store an earlier build
  wrote is corrected), not only by the editor that hides the three fields: the code that writes
  the store is the only place that can promise it. The editor shows **one notice** in place of
  them rather than three boxes nobody may fill in.
  - **Which template is the default never moves.** A *use as the default* action would buy one
    thing — moving what the bare `+` starts from — and cost a state nothing else can produce: a
    demoted default carrying a name and prompt it was never allowed to have. Editing the
    default's own fields does the same job without the hole, and `SaveTemplate` ignores an
    `isDefault` it is handed so a round-trip through the editor cannot promote anything.
- **The list is sorted by name**, case-folded and culture-aware, on both surfaces that show it.
  It is read to *find* a template, and creation order is an order only the person who made them
  knows. The default sorts among the rest rather than being pinned — a row that jumps to the top
  for a reason the eye cannot see is worse than one in the place its name puts it.
  - **Nothing badges the default.** The name *is* *Default*, unchangeably, and it is the one row
    with no delete button — a pill would say the same thing twice.
- **Two surfaces, both pages.** `/templates` is the list — the same dense-row shape the
  archive uses, and for the same reason: a template is not work in progress. `/template/new`
  and `/template/{id}` are the editor: the task form with the name added, the specific-datetime
  schedule removed, three fields hidden on the default, and a
  **Save** rather than settings' apply-as-you-type, because `/template/new` has nothing to
  apply to until the user commits it. Reached from a **bookmarks icon in the top bar**, beside
  the archive, with a pointer in Settings.
  - **The list row reads *titled "…"* when a template pins one**, because "what will my cards be
    called" is the question the list cannot otherwise answer — the row's own headline is the
    template's name, which is a different string.
- **Creating from one is a split button on the board.** The `+` in Preparing keeps its plain
  click — new task from the default — and grows a menu of the **other** templates, which
  navigates to `/card/new?template={id}`. A **query parameter, not a route**: it narrows
  `/card/new` rather than naming a different destination, and an id that no longer exists falls
  back to the default instead of 404ing the one page whose job is to make a task.
  - **The default is deliberately not in the menu**, and the caret only appears once something
    else is. The button *is* the default template, so listing it under its own caret offers the
    same thing twice — and on a fresh install that menu would have held exactly one entry
    duplicating the button it hangs off.
- **Creating a template from a task reads the form, not the card** — the opposite of
  *Duplicate*, deliberately. A copy has to be the run that was saved; a template is not a run,
  it is the shape of the fields in front of you. So *Save as a template* sits in the task
  page's footer beside Duplicate and works on **`/card/new` too**, before any card exists. It
  asks for a name in a **dialog**, by the dialogs-are-forks rule: it interrupts a save already
  under way and there is no url meaning "name the template I am about to make".
- **Stored in the settings document, not a collection of their own** (`UserSettings.Templates`).
  They are the user's choices, they are read on a render path — the board's picker draws one per
  template — and the settings document is already loaded once at startup. A collection would
  have bought a port, a store, a cache and an async load for a list that is a handful of items
  long.
- **Schema 1 is the release baseline** — the migration list is empty and every install starts on
  a store this build created. The migration machinery stays for the first breaking change after
  the release; see `docs/design-notes.md` for the rules the next entry has to obey.

## Attachments

A task can carry files: a screenshot, a log, a spec, a PDF. Three ways in, and they are
one code path — a **file picker**, **drag-and-drop**, and **paste**. The browser hands all
three over as the same `File` object, so `act-attach.js` funnels a drop and a paste into
the page's own hidden `<input type="file">` and lets Blazor's `InputFile` stream them the
way it already streams a pick. No base64 over interop, and nothing to bump the SignalR
message cap for.

- **Paths, never contents.** The opening prompt is a positional command-line argument for
  both agents, and Windows caps a command line at ~32,767 characters — so inlining a file
  would spend the whole budget on a modest log and fail the spawn outright on a large one.
  `AttachmentInstruction` appends a localised header and one **absolute path per line**, and
  the agent reads the file with its own tools. This is the thing a CLI host can do that a
  chat client cannot: the file is already on the machine the agent runs on.
  - Appended **at launch and never stored**, exactly like the `autoGit` sentence —
    `initialPrompt` stays verbatim for the life of the task, which is what the form promises.
  - Appended **by the adapter**, not the launcher, because which files a CLI can take
    natively is a fact about that CLI.
- **ACT's own directory, never the task's working directory** —
  `<dataDir>/attachments/<cardId>/`. ACT writes nothing into a user's repository, and an
  attachment written into one is one `autoGit` commit away from being pushed.
- **A copy, not a reference.** A paste has no source path at all, so referencing in place
  could not cover all three routes; and a referenced file the user later moves or deletes
  leaves a card that cannot be re-run.
- **Each agent delivers them its own way**, and both are measured:
  - **Claude Code** — `--add-dir=<folder>` on every launch *and* resume, plus every path in
    the prompt. There is no `--image` on this CLI, so a picture arrives as a path the agent
    reads; the grant is what stops `default` permission mode parking on a permission prompt
    for the task's own attachment. It is passed **whether or not anything is attached yet**,
    because a file dropped onto the terminal an hour in has no second chance at the command
    line.
  - **Codex** — `--image=<path>` per image, which puts the picture on turn one as a real
    attachment rather than behind a `view_image` call, and the rest named in the prompt.
    **No grant**: Codex reads outside `--cd` under both `read-only` and `workspace-write`
    (measured), and its own `--add-dir` makes a directory *writable*, which is not what a
    reference file wants.
  - **Both flags are `=`-bound and that spelling is load-bearing** — both are variadic,
    and a separated value eats the positional prompt after it. The measured failures are
    in `design-notes.md` → *A variadic flag must never be the last one before a
    positional*.
  - A **resume** carries the folder and drops the list: the transcript it just reopened
    already holds the files, and re-handing them would read as a fresh handover forty turns in.
- **A live session takes them too, and it is still the user typing.** A drop or a
  files-carrying paste over the Terminal tab saves the file and inserts its **quoted absolute
  path** where the cursor already is — with **no submit key**. That is the same completion of
  a drag gesture every terminal emulator performs, not ACT answering on the user's behalf:
  nothing is phrased, nothing is decided, and nothing is sent until the
  user presses Enter. A paste carrying **text** is never claimed — not even when a bitmap rides along
  with it, which is what copying a spreadsheet cell puts on the clipboard — so xterm and the prompt
  box keep handling text exactly as they did.
  A mid-session file is deliberately **not** added to `attachments[]`: the prompt is the
  opening instruction, so a file handed over later belongs to the conversation, not the record.
- **10 files, 25 MB each.** `IBrowserFile.OpenReadStream` refuses to guess a ceiling, and a
  card keeps its files until the archive is purged, so the store needs a bound. An oversized
  file is named in the message; a batch over the count is refused **whole** rather than
  truncated, because keeping the first few of a dropped selection is the kind of partial
  success nobody notices until the agent asks about a file that never arrived.
- **Written on add, reconciled on commit.** The bytes hit disk the moment a file is attached,
  so a 25 MB paste does not sit in server memory waiting for a Save that may never come — and
  a mid-session drop has no Save at all. The disk and the card are brought back into agreement
  when the form commits either way: a save prunes to what it stored, a discard prunes back to
  what the form opened with (or clears the folder outright for a card that was never saved).
  `NewTaskForm` mints the card's `id` for the same reason, so a create stages into the folder
  the saved card will own and there is no move step to get wrong.
- **Three lifecycle rules, and only one of them deletes.** A **purge** of the archive is the
  only place a card leaves the store and so the only place its files may go; a **soft delete**
  keeps them, because the archive can put the card back; a **duplicate** copies them, so the
  copy owns its own and removing an attachment from one card cannot empty the other's prompt.
  A startup **sweep** clears folders no card claims — the crash-shaped exit the discard path
  cannot cover — and it can only run there, where "no card owns this" is a fact rather than a
  race against a form halfway through its first attachment.
- **Templates carry none.** A file belongs to one run, exactly like the specific datetime a
  template already drops.
- **Hovering an image chip shows a thumbnail** — the cheapest way to
  answer "which screenshot is that", which a filename like `pasted-20260804-232132.png` cannot.
  - **Rendered only for the chip under the cursor**, not hidden behind CSS on all of them: ten
    attachments would otherwise be ten image fetches on first paint, one of them possibly 25 MB.
  - **Images only.** A log or a PDF has no thumbnail, and an empty frame under the cursor reads
    as a broken image rather than as "nothing to preview".
  - **It works on a locked card too**, which is where it is most wanted: a launched or signed-off
    task is the one you come back to in order to read what it was given. The files are still there —
    only an archive purge removes them. It regressed once because `act-attach.js` began life as the
    drop/paste module and was imported only for an *unlocked* card; `place` lives in that module, so
    a completed card rendered a preview nothing ever positioned or revealed. The import is now
    unconditional and only the file-input half is gated on `Locked`.
  - **A fixed-size box, `position: fixed`, placed by JS.** The sheet is the page's scroll
    container, so an absolutely positioned overlay is clipped by it — measured, not assumed:
    opening downward was cut off by ~200px in a 720px window, and opening upward would be cut
    off on a taller form, so *neither* direction is safe. `act-attach.place` puts the box beside
    the chip, flips it above when there is no room below, and clamps it to the viewport. The box
    is sized in CSS rather than by the image, which is what lets the position be computed once,
    before the bytes arrive, with no second pass and no jump.
  - The bytes come from `/attachments/{cardId}/{fileName}` — see *Local-endpoint security*.
- **Clicking an attachment opens it in whatever the OS associates with it** — on both
  faces and for every type including images. It is the answer to the half the thumbnail cannot cover: a
  log, a PDF, a spreadsheet were previously attachable and then impossible to look at.
  - **Through `IDesktopBridge.OpenPathAsync`**, which is `shell.openPath` on the desktop and
    `Process.Start` with `UseShellExecute` in browser mode — the latter opens on the *server*, which for
    ACT is the machine the user is sitting at. Same trust model the logs folder already opens under.
    It replaced `OpenFolderAsync`: a file and a folder are one operation to the shell, and the old name
    made every call site handed a file read as a bug.
  - **A refusal is reported, not thrown.** An extension with no associated program is a shrug from the
    OS rather than an exception, and a click that appears to do nothing is the worst version of that —
    so the bridge answers with a message and the two failures are told apart: the file is *gone* (the
    card's folder no longer holds it, which a purge can do underneath an open page) or the OS *would
    not* open it.
  - A real `<button>`, not a clickable row, so it answers the keyboard; the remove **X** is a sibling
    rather than a child, so clicking it cannot also open the file.
- **The terminal face lists them too** — in the rail beside the agent, directory and
  session id. The paths are in the opening prompt, but by the time anyone asks "what was it given?"
  the prompt has scrolled off the top. It shows the **card's** list rather than the folder's contents:
  a file dropped into the terminal mid-session belongs to the conversation and is already on screen a
  few lines up. Hidden entirely when there are none, like the metrics beside it.
  - **Hovering a row previews it there too**, which is the face that wants it most: reading a
    screenshot while watching the terminal beats navigating away to look at it. The 22rem box is wider
    than the 15rem rail, and that is fine — `place` clamps it to the viewport, so it opens leftward
    over the terminal rather than off the edge.
  - **It works on an archived card**, which renders no terminal at all. The files outlive the session
    and only a purge removes them, so the module carrying `place` is loaded whether or not there is a
    terminal to drop onto — the same gate that broke the task form's preview on a completed card.

## Auto-generated titles

Nobody should have to name a task twice. The prompt already says what the work is,
so `title` stopped being a field the user must fill in: it is **written from the
prompt by the task's own agent**, on demand from a button beside the box and
automatically on a save that leaves it blank. Typing one still wins — the box is
first, editable, and never overwritten except by an explicit click. A **template**
that carries a title fills the box the same way typing would, so creating from one
of those costs nothing at all (see *Task templates*).

**The automatic half is a setting** — `generateTitles`, *General*, on by default. It is the one
piece of ACT's behaviour that spends the user's quota without being asked, so it is the one that
gets a switch; the **button is outside it**, because a click there is the user asking rather than
ACT deciding. With it off, a save that leaves the box blank stores a **blank title** and asks
nobody: filling it in anyway would leave the switch governing nothing. What keeps the board
readable then is `CardTitle` — the one function every surface asks for a card's name, which
answers with the stored title and falls back to the prompt's opening words when there is none.
Two properties follow from doing it at render time rather than on save:

- **Nothing derived is ever stored.** Editing the prompt renames the card with it, and a title
  the user types replaces the stand-in immediately. `Duplicate` copies an untitled card as
  untitled for the same reason — `Copy of …` over a derived name would be the one place a
  stand-in got frozen into a stored card.
- **The stand-in is the same string the save used to write**, so switching the setting changes what
  is *stored* without changing what any screen says. Search is unaffected either way: `CardSearch`
  already matches the prompt.

The rest of this section describes the generated title, which is what the button does and what a
save does while the setting is on.

The title is the *only* reason ACT ever runs a CLI on its own account, and the
whole design is about making that cost nothing:

- **A query, not a session.** `ICommandHost` is the second way ACT starts a
  process: one short-lived run, both streams captured separately, no terminal, no
  session, no hooks, no card. It sits beside `IPtyHost` as a Core port for the same
  reasons, and `CommandHost` implements it. This is **not** the rejected stream-json
  control protocol below — no interactive session is replaced, because none is
  involved.
- **The cheapest thing the agent has.** `AgentCapabilities.UtilityModel` is a
  second default the user never chooses — Claude Code's `haiku`, Codex's
  `gpt-5.4-mini` — run at the bottom rung of that model's own effort ladder. The
  caller cannot make it expensive: `AgentQueryRequest` carries no model, no effort
  and no permission mode, and the machine's `extraFlags` are deliberately ignored
  so a stray `--model` cannot redirect it.
- **Nowhere to run, and nothing to run with.** Both CLIs read the directory they
  start in — CLAUDE.md, AGENTS.md, project settings, the enclosing repo — so a query
  runs in an empty ACT-owned scratch directory with every customization switched off
  (`--safe-mode`; `--ignore-user-config`) and, on Claude Code, **every tool disabled**
  (`--tools ""`). None of that context is worth paying for to name a sentence — the
  tool schemas alone are **~25,000 of the ~30,000 tokens** a title would otherwise cost.
- **A title costs ~5,000–7,000 tokens, and the prompt is not what drives it** — the
  CLI's own preamble is, so a long prompt is never truncated before asking. The
  measured breakdown per agent is in `docs/findings/agent-title.md`.
- **A save never waits for it.** The query takes five to seven seconds, and a save
  is not allowed to cost that: the card lands on the board immediately, titled with
  the prompt's opening words, and `TitleBackfill` runs the query behind it — the
  strip shows a small spinner beside the placeholder while the answer is out, and
  the agent's title replaces it when it arrives. The backfill only ever replaces
  the exact placeholder it was started with, so a rename made in the meantime wins,
  and a later save that types a title withdraws the pending answer outright. The
  pending state is deliberately in memory only: the stored card is always properly
  titled, so a restart mid-query simply keeps the placeholder. The button beside
  the box is the one place that still waits in place — the user asked a question
  there, and the box is where the answer goes.
- **It never fails.** A missing CLI, a refusal, an expired login, an exhausted
  quota, a timeout or an unusable answer falls back to the prompt's own opening
  words — which the save already wrote, so a failed backfill just stops the
  spinner and keeps them. A save refused over a field ACT offered to fill in would
  be worse than the form that made the user type a title. The button says so with
  a toast; a save does it silently, because the user was saving rather than asking
  a question.
- **A card is never nameless — the guarantee simply moved.** The field carries a validator
  that accepts blank *while there is a prompt to derive one from*, and refuses the save when
  both are empty; a plain required validator would have refused before the code that writes
  the title ever ran. That rule holds whichever way the setting is set, because the prompt is
  what the name comes from either way. What the setting changes is *where* the promise is kept:
  with generation on it is `NewTaskForm.ApplyTo`, alongside the launch-boundary gate, so the
  empty string is a value a stored `title` can never hold whatever the CLI did; with it off the
  store may hold one and `CardTitle` answers for it at every render instead.
  `TaskTitleQuery.FromPrompt` therefore answers for **any** prompt with a character in it,
  including one made entirely of the punctuation it would otherwise strip.
- **What comes back is not trusted.** `TaskTitleQuery` strips the quotes, markdown,
  bullets, preamble and trailing period a chatty model wraps a one-line answer in,
  then caps it at thirty words and the form's own 200 characters.

The measured flag sets, the traps behind each one (notably why `claude --bare`
would break auth, and why Codex's prompt must go on stdin), and the re-test
checklist are in `docs/findings/agent-title.md`.
