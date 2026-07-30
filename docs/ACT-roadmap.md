# ACT — Implementation roadmap

Build sequence for ACT. Companion to the spec (`ACT-overview.md`); this file is
build-tracking, not design. Steps are ordered so each is small, independently
verifiable, and leaves something runnable.

## Guiding principles

- **Shell first, release script second** — establish a repeatable release build
  early so every step after stays shippable.
- **Abstraction-first, both agents together** — introduce the agent-adapter seam
  as soon as we touch launching, then implement **Claude Code and Codex in
  parallel** against it. Forces a real abstraction instead of a Claude-shaped one
  (cost: bigger agent-steps; both CLIs needed to test).
- **Host the real TUI; never re-implement it.** ACT runs the agent's interactive
  terminal under a PTY and observes it through hooks. It never parses the screen and
  never answers a prompt on the user's behalf — see the spec's *Liveness & the
  embedded terminal*. Steps 7–10 were reshaped around this on 2026-07-29.
- **Observability before automation** — get sessions *visible* and manually
  drivable on the board before adding scheduling / auto-execute / overnight.
- **Spine before breadth** — full end-to-end loop working before polish and
  extra features.

## Phase 1 — Foundation

- [x] **1. Empty app shell** — Blazor Server on .NET 10, runs via `dotnet run`,
  blank page. *Verify:* app loads in browser. ✅ Loads at `http://localhost:5210`
  (title "ACT — Agent Control Tower"), clean build, no console errors.
- [x] **2. Release build script + CI** — repeatable release build; wire
  Electron.NET so it also produces the desktop artifact; stand up the **test
  projects (xUnit) and GitHub Actions CI** now, so the fast suite runs from step
  one. **While the repo is private:** run **Linux-only CI on push/PR** and the
  **Windows leg occasionally** (pre-release or weekly) to stay inside the free
  2,000-minute tier (Windows drains at 2×); switch on the full Windows + Linux
  matrix at go-public (then free and uncapped). *Verify:* one command yields a
  runnable desktop build; CI is green on an empty test. (Established now so
  everything after ships and is tested.) ✅ `build.ps1` (restore/build/test) +
  `package-desktop.ps1` (NSIS installer, self-contained); 4 xUnit projects green;
  `ci.yml` green on Ubuntu; `release.yml` produced a runnable Windows installer.
- [x] **3. LiteDB + card model** — persist the full card model; seed fake cards.
  *Verify:* cards survive restart. ✅ `Card` carries the full spec field set
  (identity, state, launch config, lineage, transitions, metrics); `ICardStore`
  port + `LiteDbCardStore`; a schema-version document with a migration hook; the
  friendly `number` sequence starts at **#1000** from a counter document. Sample
  cards seeded **once into an empty store**, and were **`Debug`-only — `Seeding/` was
  excluded from the Release compile**, so no demo data ever shipped. Board and the
  top-bar attention count read from the store; a restart returns the same board
  (no re-seed, no duplicates). **Seeding was removed once tasks became easy to
  create** (step 7's task page); an existing `act.db` keeps whatever it was already
  given.

## Phase 2 — Board (read-only, agent-agnostic)

- [x] **4. Static board** — six flat columns, flight strips, compact/spacious
  toggle, rendering seeded cards. Pure UI, no behavior. *Verify:* board reflects
  store; density toggle works. ✅ Six columns with live counts, flight strips
  covering all eight rail states, both densities rendering from the LiteDB store
  via `ICardStore`. Density is chosen in the settings dialog (no top-bar toggle)
  and survives a restart. Compact shows the **badge**, not a dot, per the spec —
  the title ellipsizes to give the badge its width, verified down to the 210px
  minimum column with no overflow and no clash with the `!` attention corner.
- [x] **5. Task creation + manual moves** — new-task modal → card in Preparing;
  drag Preparing ↔ Ready with drag-time graying of invalid columns. *Verify:*
  can hand-manage cards through the human-controlled columns. ✅ Cards can be
  created, reopened for editing, and dragged between the two human columns, with
  every change persisted.
  - [x] **Creation** — new-task modal with the full control set (title, prompt,
    dir, agent, model, effort, permission mode, schedule, auto-complete,
    auto-git; tools / flags / env behind *Advanced*), required-field validation,
    single Save → card lands in Preparing with a freshly minted number. A
    `BoardState` service owns the card list and raises `Changed`, so the board
    updates live. `model` / `effort` now come through `IAgentCapabilityCatalog`
    (step 6), whose implementation stays a **flagged placeholder** until the real
    adapters supply the values at step 7.
  - [x] **Editing** — clicking (or keyboard-activating) any card reopens the same
    dialog prefilled, and Save updates that card in place. `NewTaskForm.ApplyTo`
    writes only the fields the form owns, so id, number, session, column, badge,
    lineage, transitions and metrics survive an edit. **Not yet gated by column**
    — every field is editable in every column, which conflicts with the
    immutable-`initialPrompt` rule for launched cards; gating comes with the
    drawer (step 10), which also replaces card-click as the way in.
  - [x] **Manual moves** — HTML5 drag and drop between **Preparing ↔ Ready**.
    `Act.Core/Rules/ManualMove` owns the validity rules (unit-tested across all 36
    column pairs); only human-controlled cards carry `draggable`, machine cards
    do not lift at all. On pick-up the valid target lights and every invalid
    column grays to 40%; the source column stays neutral. A drop appends a
    `Transition` and persists via `ICardStore.UpdateAsync`.
    `Ready → Executing` (launch, step 7) and `Completed → To review`
    (reopen, step 11) are deliberately **not** manual moves yet.

## Phase 3 — Agent abstraction + both adapters

- [x] **6. Agent adapter interface** — define the seam only (no behavior):
  launch, ingestion sources, the input channel, the **injected preamble** (teaches
  the agent the `.act/` status + follow-up file conventions — the agent↔ACT
  contract that steps 8–9 and 12 depend on), and normalized mappings
  (`model` / `effort` / `permissionMode`, permission/question events,
  unsupported-value behavior). Ship a **mock adapter** for tests. *Verify:* mock
  adapter drives a fake session through the states. ✅ `Act.Core/Events/` holds the
  normalized vocabulary every source funnels into. `Abstractions/` holds
  `IAgentAdapter` (identity, `AgentCapabilities`, `Resolve`, launch/resume),
  `IAgentSession` (event stream out; *originally* approve/deny, answer, send,
  interrupt, kill in, where disposal was the between-turns teardown — **that whole
  input surface was superseded by the PTY decision; see the revision below**),
  `IIngestionSource`, `IAgentCapabilityCatalog` and `IClock`. `LaunchConfigResolution` makes the
  never-silently-drop rule mechanical: an unsupported value is **substituted with a
  recorded adjustment or rejected with a message**, and there is no third outcome.
  `Act.Core/Agents/` owns the `.act/` paths and the preamble text (documented in the
  spec's *Agent ↔ ACT contract*). `Act.TestSupport` ships `MockAgentAdapter` +
  `AgentScript` + `TestClock`; `AgentAdapterContract` in `Act.Agents.Tests` is the
  suite every adapter must pass, run twice — once Claude-shaped, once as a narrow
  Codex-shaped agent with no reasoning effort and two permission modes. Per the
  testing standard the suite covers only the **process-free** surface; a scripted
  session proves the lifecycle (started → activity → permission observed →
  keystroke in the terminal → turn ended → session ended, plus kill and disposal).
  **No column or badge moves yet** —
  the rules engine that reads these events is step 9, which is where the full
  walk-the-states assertion lands.
  - **Revised 2026-07-29 for the PTY decision.** The seam shipped assuming ACT
    would answer prompts over a stream-json control channel; the launch prototype
    reversed that. `IAgentSession` **lost** `RespondToPermissionAsync`,
    `AnswerAsync`, `SendAsync` and `InterruptAsync` (and `PermissionDecision` was
    deleted outright — ACT decides nothing), and **gained** `IAgentTerminal`
    (raw read/write, resize, bounded scrollback replay, and `SubmitAsync` for the
    only text ACT types itself — step 7 cut that down to the send-back message alone).
    New port `IPtyHost` keeps the pseudo-terminal
    primitive in `Act.Core/Abstractions` and its `Porta.Pty` implementation in
    `Act.Infrastructure`, so adapters never depend sideways on infrastructure.
    `IAgentAdapter` gained `DesktopHandoffUrl`, `AgentCapabilities` gained
    `DesktopHandoff`, and both requests carry a `TerminalSize` (a pty must be sized
    at spawn). Disposal now **ends** the session rather than being a between-turns
    teardown.
- [x] **7. Launch under an embedded PTY — Claude Code + Codex** — Ready → Executing
  spawns each agent's **interactive TUI** under a pseudo-terminal ACT owns, with a
  pre-minted `--session-id` and the **injected preamble** (so the agent knows the
  `.act/` conventions from turn one); bind session →
  card; a **minimal full-screen terminal view** per card to see it in (xterm.js,
  resize, scrollback replay); the initial prompt rides the launch command line.
  *Verify:* both real agents launch inside ACT, the TUI is usable (typing, resize,
  leaving and re-entering the view), the preamble is visible in the session, and the
  card binds. ✅ Lifted `PtySession` + `prototype-pty.js` from the `prototype` branch,
  **without** `PtyStateProbe`.
  - [x] **Adapters + PTY host.** `IPtyHost`/`PtyProcess` over `Porta.Pty`;
    `Act.Agents.ClaudeCode` and `Act.Agents.Codex` both against the contract suite;
    `PtyAgentSession`/`PtyAgentTerminal` shared in the core; capabilities now come
    from the registered adapters, retiring `PlaceholderAgentCapabilityCatalog`.
    Verified live: both CLIs launch under the pty, paint, take a keystroke, and
    carry the preamble; Claude Code binds its pre-minted id, Codex reports null as
    designed.
  - [x] **Session view + Ready → Executing wiring.** Route `/card/{id}/terminal`
    (xterm + side rail + back-to-board), `SessionRegistry` holding the live sessions
    so the route re-attaches instead of re-launching, `SessionLauncher` for the
    Ready → Executing action, and a Launch button on spacious Ready strips. Past the
    launch boundary a card click opens its terminal instead of the edit form.
    Verified live end to end: Claude Code spawned under the pty (correctly parented,
    resolved off `PATH`, conhost geometry matching xterm's 140×43), the card moved to
    Executing and persisted its session id, and re-entering the route replayed the
    TUI out of `Terminal.Backlog` into the xterm buffer.
    - **A pseudo-console outlives the process attached to it.** `IPtyConnection` is
    `IDisposable`, and killing the agent without disposing it leaks one `conhost.exe`
    per session ACT ever launches. Found by watching the process tree after a kill;
    `PtyProcess.DisposeAsync` now disposes the connection last.
  - [x] **Painting verified in a real window, and the typed opening prompt replaced.**
    The preview pane reports `document.hidden`, so it never fires
    `requestAnimationFrame` and xterm's render debouncer never runs — the buffer fills
    while the rows stay blank. That is the pane, not the app: driven in a real browser
    window, the TUI paints, the rail sits where it belongs, and the layout does not
    overflow. Verifying it there is what exposed the launch bug below, which the
    headless pane had been hiding.
    - ⚠️ **And then it still was not reaching the agent — the argument was being truncated.**
      Found 2026-07-30 from the terminal view: the session showed part of the preamble, cut at
      `{ "state:`, and the agent greeted the user instead of working. The transcript confirmed it
      received **677 characters and no task prompt at all**, every launch since the positional
      prompt landed. `Porta.Pty` wraps each argument in quotes and escapes the content by
      doubling quotes (`"` → `""`), which Claude Code's parser does not read back as a literal
      quote — it ends the argument there. Since the preamble always contains
      `{ "state": "ready_for_review" }`, every launch was cut at that point.
      **Fixed** by taking the job over: `PtyOptions.VerbatimCommandLine` stops the transport
      escaping, and `Act.Infrastructure/Terminal/WindowsArgument` quotes each argument by the
      rules `CommandLineToArgvW` documents. Measured against the real CLI: 226 characters in,
      226 out, where the transport's own escaping gave 28; then end to end through ACT, an
      1825-character opening prompt arrived intact with a quote-bearing task prompt verbatim.
      Unit-tested against a local implementation of the split rules, including paths that end in
      a backslash.
      - **What let it hide for two steps:** every verification asked whether events arrived and
        whether the card moved, and both were true — a session that only greets the user still
        emits `Stop` and still lands in To review. Checking the *transcript* for the task text is
        the assertion that would have caught it, and is what the fix is verified with.
    - **The opening prompt was never reaching the agent (the first cause).** It was typed —
      first output counted as "painted", 300ms of quiet counted as "ready", then bracketed paste
      + submit key. A CLI that has painted its banner is *not* yet listening: the paste
      went nowhere, the composer sat empty at its placeholder, and the board happily
      showed `running`. The write path itself was fine (typing into the same session by
      hand landed immediately), so the flaw was the readiness guess — and no better
      guess exists, because knowing when the prompt line is live means reading the
      screen, which ACT does not do. **Fixed by deleting the guess:** Claude Code takes
      the prompt positionally (`claude [prompt]`) exactly as Codex already did, so it is
      in place before the TUI paints. `PtyAgentSession.Open` is gone with it;
      `IAgentTerminal.SubmitAsync` stays for step 11's send-back, where the TUI has been
      idle for as long as the user took to decide. Regression-guarded in
      `ClaudeCodeAdapterTests` — prompt positional, and nothing written at launch.
- [ ] **8. Ingestion — observability only** — normalized event stream fed per
  adapter (Claude: http hooks + files + process; **Codex: files + process only** — its
  hooks do not fire on the current CLI, see *Codex hook findings*), over a localhost hook
  endpoint with a **per-session token**.
  **Includes the hook config injection itself** — Claude's `--settings` file and Codex's
  `--profile` layer — which step 7 deliberately left out: a settings file pointing at a
  hook host that does not exist yet buys nothing and only risks clobbering the user's own
  `.claude/settings.json` a step early.
  Card shows live `running` / activity / metrics. **Nothing is ever posted back to
  the agent** — every source reports, none command. *Verify:* live state + metrics
  update for both agents; a permission prompt in the terminal raises the badge
  without ACT touching the prompt.
  - **Endpoint shape — decided 2026-07-29.** One loopback endpoint on ACT's own web
    host, shared by every session and both agents; see the spec's *Local-endpoint
    security* for the reasoning.
    - **One port, kept across restarts.** Bind `127.0.0.1:0`, store the assigned port,
      reuse it next start, take a fresh one only if it is taken. Not a port per session
      (more listeners, no isolation the token does not already give) and not fresh each
      start (see the trust-hash item below). Off the app's own port so the hook surface
      is not the one the browser uses.
    - **`/hooks/claude` and `/hooks/codex`.** The path picks the parser, because the two
      payload dialects are different; a payload on the wrong route is a 400, not a
      silent mis-parse. Correlation to a card comes from the payload's `session_id` plus
      `ACT_TASK_ID` on the process environment — never from the port.
    - **The URL rides the environment, not just the token.** Codex hashes the hook
      *definition*; anything in the command string that changes between launches costs a
      fresh trust prompt. So the command is a fixed template reading `$ACT_HOOK_URL` and
      `$ACT_HOOK_TOKEN` from the env ACT sets on the PTY process. Claude has no such
      constraint — its `--settings` file is rewritten per launch anyway.
  - ✅ **Endpoint + hook injection landed; Claude ingestion verified live.** Two loopback
    listeners on the one web host (UI on the app's port, `/hooks/claude` and `/hooks/codex`
    on a kept port), per-session tokens, a normalizer per adapter, and generated hook config
    per launch. Verified end to end: a launched card's hooks posted and normalized to
    `ActivityObserved` ×3 then `TurnEnded`; the app port answers 404 for `/hooks/*` and the
    hook port 404 for the UI; an unknown token is 401; ending the session releases the token
    (401 after) and deletes the generated settings file.
    - **Two Kestrel traps, each of which moved the whole UI onto the hook port before being
      fixed.** Configured addresses and explicit `Listen` endpoints are mutually exclusive,
      so the app's own address is read back out of the `urls` configuration key and re-added
      alongside the hook one. And `UseStatusCodePagesWithReExecute` re-executes a hook
      rejection through the Blazor pipeline, which answers a json post *"incorrect
      Content-type"* 400 instead of the intended 401 — so the hook endpoint turns the
      status-code-pages feature off per request.
    - **The shared-vs-per-task split is load-bearing, and a test caught it.** Claude's
      settings file is per task because it carries the token; Codex's forwarder and hooks
      json are **shared**, because the forwarder path appears inside the definition Codex
      hashes — a per-task path would mean a trust prompt for every card ever created.
  - **Open questions for this step** (each decides a fallback, so settle them first):
    - ✅ **`--settings` accepts `type: "http"` hooks, and they carry a custom header —
      verified live.** The token rides `x-act-hook-token`; posts authorized. So Claude needs
      no command forwarder, and the token never has to go in a url. The flag takes a path and
      ACT generates its own file per launch, so the user's committed `.claude/settings.json`
      is never read or written.
    - ⚠️ **`SessionStart` was not observed arriving**, though `UserPromptSubmit`, the tool
      events and `Stop` all were. Not load-bearing for Claude Code — ACT pre-mints the
      session id, so that payload was only wanted for `transcript_path` and `cwd`, which the
      file source can supply — but do not rely on it without re-checking.
    - ✅ **`Notification` fires an idle nudge, measured 2026-07-30** — message exactly
      *"Claude is waiting for your input"*, about a minute after a turn ends, on
      `claude-code v2.1.220`. Caught by leaving a finished card untouched and watching the
      endpoint log. **It broke the first classifier:** treating "waiting for your input" as a
      permission prompt badged an idle card `needs permission`, with nothing to approve, and
      dragged it out of To review. An unrecognised notification now produces **no event** —
      calling it activity would have been worse still, since an idle nudge means the opposite
      of activity and would send a reviewed card back to Executing by itself.
    - ✅ **A real permission prompt reports as `Claude needs your permission`** — observed
      2026-07-30, once the prompt-truncation fix let an agent actually start work and hit a write
      gate. The classifier keys on "permission" / "approve", so it routed correctly: the card
      moved to Needs feedback with `needs permission`, from a prompt ACT never touched. Both
      halves of the `Notification` question are now measured rather than assumed.
    - ✅ **Codex hooks — tested, and they do not fire at all** on `0.146.0-alpha.3.1`; see
      *Codex hook findings*. This answers the two Codex questions that were here (env
      expansion, `--profile` layering) by making them moot for now, and **changes this
      step's plan: Codex ingestion starts from files + process signals**, with hooks as a
      later upgrade. It also removes `PermissionRequest` as a usable signal, so Codex has
      *no* event-based permission report either — for now both agents depend on what the
      files and the process can tell ACT.
- [ ] **9. Rules engine** — agent-agnostic: normalized events → column/badge
  transitions; status-file convention (`ready_for_review` / `needs_input`) routes
  Executing → To review / Needs feedback; `error`/`stale` from process signals;
  **observed** permission/question in, and **observed activity** (`UserPromptSubmit`)
  as the recovery out of Needs feedback and the send-back out of To review — since
  the user acts in the terminal, ACT only ever learns about it.
  *Verify:* a task walks the machine-region states correctly.
  - [x] **The engine itself, brought forward with step 8's pump.** `Act.Core/Rules/RulesEngine`
    is the spec's transition table as pure logic, `MetricsProjection` is the numbers half, and
    `Act.App/Sessions/SessionEventPump` is the only stateful part — one drain loop per session,
    persisting immediately on a move and on a debounced tick for metrics alone (a move is
    user-visible; `BoardState.UpdateAsync` re-reads every card, so tool events at several per
    second must not each re-render the board). 35 tests in `Act.Core.Tests`, including that no
    event can produce Ready, Preparing or Completed — the launch boundary and the completion
    call are not the machine's to make — and that cards outside the machine region are never
    moved by any event.
    Verified live: a launched card walked Ready → Executing (`running`) → To review (`idle`)
    on its own, with the turn count landing on the strip.
  - **Still step 9's to finish:** the outcomes that need sources step 8 has not built yet
    (`ready_for_review` / `needs_input` need the status-file source, `stale` needs the
    watchdog), and the `Notification` classification below.
- [ ] **10. Session view + card actions — both adapters** — the full session view
  (terminal + right rail: identity, cwd, agent/model, context %, cost, turns;
  back-to-board) and the drawer's **read-only** blocked-card presentation with
  "Answer in terminal ›". Actions: Open terminal, **Open in Desktop**
  (`claude://resume?session=`), Kill, Retry, Send back, Complete. Explicitly **no
  approve/deny anywhere**. *Verify:* a task goes full-circle by hand on both agents,
  answering every prompt in the embedded terminal.

## Phase 4 — Completion & lineage

- [ ] **11. Complete + reopen + auto-complete** — To review → Completed and
  reopen; `autoComplete` and in-session `autoGit`. *Verify:* clean-finish tasks
  self-complete; reopen resumes.
- [ ] **12. Spawning & lineage** — `.act/followups/` ingestion, parent/children,
  the git-on-completion spawned task. *Verify:* a task spawns tracked follow-ups
  with correct lineage.

## Phase 5 — Automation (on a proven base)

- [ ] **13. Native notifications** — OS pings for attention states; actionable,
  focus-aware, coalesced. *Verify:* backgrounded ACT pings on needs-you.
- [ ] **14. Scheduling & queue runner** — `schedule`, `maxConcurrent`,
  `dependsOn` ordering, rate-limit backpressure (`waiting-reset`), keep-awake,
  and the global **auto-execution pause switch**. *Verify:* a queue of Ready
  tasks processes unattended across a window reset; pause halts all auto-launch.

## Phase 6 — Breadth & polish

- [ ] **15. Persistence polish** — auto-archive (90-day default), search,
  transitions timeline in the drawer. *Verify:* old Completed cards archive and
  stay searchable.
- [ ] **16. UI polish** — final spacing, type, Radzen theming pass.
  - **User settings page** — consolidate the preferences that landed scattered
    across features into one screen: **theme** (Radzen *Standard* / *Standard
    Dark*; default **follows the OS** light/dark preference), density default,
    notification matrix, keep-awake, `maxConcurrent`, weekly-reset time,
    auto-archive window, auto-execution pause. (Each knob works from its own
    feature step; this just gives them a home. The theme override replaces the
    interim OS-only auto-switch wired at Radzen setup.)

## Milestones

- **After step 10** — usable, manually-driven, **multi-agent** ACT (a plausible
  v1): every session runs in its own embedded terminal, the board tells you which
  one needs you, and you answer it there.
- **After step 14** — the overnight unattended batch vision.
- **After step 16** — the full, polished product.

## Build-time items to verify (from the spec)

- **The hook-injection unknowns now live with step 8** — `--settings` and `type: "http"`,
  http-hook headers, Codex env expansion and `--profile` hook tables, and what
  `Notification` fires for. They each decide a fallback in that step's design, so they are
  listed there rather than duplicated here. The prototype's `PrototypeHookSettings` wrote
  both an `http` and a `command`/`curl` variant precisely because the first is unconfirmed.
- **ConPTY behaviour** — resize while the TUI is mid-render. Bracketed paste is no
  longer load-bearing at launch (the prompt is a launch argument); it matters for
  step 11's send-back into a live session.
- ✅ **Both TUIs run under ConPTY — verified** by launching each through
  `PtyHost` and answering its prompt with a written keystroke. Two findings came out
  of it:
  - **A pty does not walk `PATH`.** It spawns with an explicit image path, so a bare
    `claude` / `codex` is looked for in the working directory and fails. Resolution
    lives in `Act.Infrastructure/Terminal/ExecutableResolver` — the one layer allowed
    to know about `PATHEXT`.
  - **Both agents open with a directory-trust prompt** on a working directory they
    have not seen before ("Do you trust the contents of this directory?" /
    "Is this a project you created or one you trust?"), *before* the session starts.
    ACT must not treat that first screen as a hang, and the card should say what is
    waiting. It is answered in the terminal like everything else — but note it gates
    project-local config and hooks on Codex, so it is load-bearing for step 8.
- ✅ **`claude://resume?session=<uuid>` — verified** by the prototype against the
  desktop app's own `app.asar` (v1.24012.9.0): it validates a canonical UUID and
  calls `importCliSession`. Parameter is `session`, not `sessionId`; no `cwd` is
  read. Named failure modes: `transcript_missing`, `auth_expired`, `network`.
- `/usage` scriptability + reset-time detection; clean mid-task resume after a
  rate-limit interruption.
- ✅ **Codex equivalents — resolved** against the installed CLI (`codex-cli
  0.146.0-alpha.3.1`) and the official docs; see *Codex facts* below. Still open for
  Codex: how its TUI behaves under a pty, and its submit key for `SubmitAsync`.
- ✅ **`Porta.Pty` and `xterm.js` licenses — checked.** Both **MIT**, both recorded in
  `THIRD-PARTY-NOTICES.md`. The xterm files are vendored rather than package-referenced,
  and were verified byte-exact against the published `@xterm/xterm` **5.5.0** and
  `@xterm/addon-fit` **0.10.0**; versions, hashes and a re-verification script live in
  `src/Act.App/wwwroot/lib/xterm/VENDOR.md` (the minified bundles carry no version
  string, so that file is the only record).

### Codex facts (verified 2026-07-29, `codex-cli 0.146.0-alpha.3.1`)

Read off `codex --help`, `codex debug models`, and the hooks reference. Recorded
here because three of them contradict assumptions the spec made for Claude Code.

- **No `--session-id`. ACT cannot pre-mint the binding.** Codex mints its own id;
  `codex resume <SESSION_ID|name>` and `codex fork <SESSION_ID>` take a UUID
  afterwards. The plan was to learn the id from the **`SessionStart` hook payload**, but
  hooks do not fire on the current CLI (see *Codex hook findings*), so **the fallback is
  now the primary path**: sessions land in
  `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`, indexed by
  `~/.codex/session_index.jsonl` (`{id, thread_name, updated_at}`). ACT binds by watching
  for the rollout file whose `cwd` and start time match the launch it just made, so
  `Card.sessionId` stays null from launch until that file appears.
- **Hooks are a close cousin of Claude Code's**, and `PermissionRequest` is an
  **explicit event** rather than something to infer from `Notification`.
  Events: `SessionStart`, `SessionEnd`, `SubagentStart` (session-scoped);
  `UserPromptSubmit`, `PreToolUse`, `PermissionRequest`, `PostToolUse`, `PreCompact`,
  `PostCompact`, `SubagentStop`, `Stop` (turn-scoped). Every payload carries
  `session_id`, `cwd`, `hook_event_name`, `model`, `permission_mode`, `turn_id`,
  `transcript_path`. **`type: "command"` only — no `http` type**, so Codex uses the
  command-forwarder source exactly as the spec predicted.
- **Hook trust, and why ACT's hook command must be byte-stable.** Codex hashes each
  hook *definition* and silently skips untrusted ones; approval persists
  across sessions and is re-triggered only when the definition changes. So the
  per-session token must ride an **environment variable on the PTY process** (ACT owns
  the process env) and never be baked into the command string — otherwise every launch
  is a new hash and a fresh approval prompt. ACT's hooks are observability-only, so
  they must always return success and never `permissionDecision: "deny"`,
  `decision: "block"`, or exit code 2. **Measured — see the hook findings below.**
- ⚠️ **Hooks are NOT `[[hooks.*]]` tables in `config.toml`, and on this build they never
  run.** Tested end to end on 2026-07-29 (details in *Codex hook findings* below). The two
  bullets above and the `--profile` claim were written from the docs and are wrong about
  where hooks live; the `--profile` mechanism itself is unaffected and still how
  `model_reasoning_effort` and the rest are layered.
- **Config injection is `--profile`.** `-p <name>` layers
  `$CODEX_HOME/<name>.config.toml` over the user's config, and the user's
  `~/.codex/config.toml` is never edited *by ACT* (Codex itself writes hook-trust state
  into it — see below). ACT must clean its profile files up.
- **No `--effort` flag; effort is a config key and is per-model.** Set via
  `-c model_reasoning_effort=<value>`. From `codex debug models`:

  | Model | Efforts | Default |
  |---|---|---|
  | `gpt-5.6-terra` | low, medium, high, xhigh, max, ultra | medium |
  | `gpt-5.6-luna` | low, medium, high, xhigh, max | medium |
  | `gpt-5.5` | low, medium, high, xhigh | medium |
  | `gpt-5.4-mini` | low, medium, high, xhigh | medium |

  `codex-auto-review` is `visibility: hide` and is not offered.
- **`permissionMode` maps onto two axes** — `-a/--ask-for-approval`
  (`untrusted` / `on-request` / `never`) crossed with `-s/--sandbox`
  (`read-only` / `workspace-write` / `danger-full-access`), plus
  `--dangerously-bypass-approvals-and-sandbox`. See the mapping table in the spec.
- **`--no-alt-screen`** runs the TUI inline, preserving terminal scrollback — worth
  testing against xterm.js, since ACT keeps its own scrollback buffer.
- `notify = [...]` is a separate fire-and-forget mechanism that fires only for
  `agent-turn-complete`; the hook set is strictly richer, so ACT ignores `notify`.

### Codex hook findings (measured 2026-07-29, `codex-cli 0.146.0-alpha.3.1`)

Tested against the real CLI, not the docs. **Bottom line: step 8 cannot rely on Codex
hooks on this build.** What was established:

- **Hooks live in a JSON file, not in `config.toml` tables.** `hooks` is a *string* key
  holding an absolute path (`hooks = "/abs/path/hooks.json"`); a `[[hooks.SessionStart]]`
  table fails with `invalid type: sequence, expected a string`.
  **`$CODEX_HOME/hooks.json` is auto-discovered** with no config key at all — proven by
  corrupting it and getting `warning: failed to parse hooks config …` on every session
  start. Schema:

      { "description": "…",
        "hooks": { "SessionStart": [ { "matcher": "startup",
                     "hooks": [ { "type": "command", "command": "…" } ] } ] } }

  The file **must be BOM-free** — PowerShell's `Out-File -Encoding utf8` writes a BOM and
  the parser dies at `line 1 column 1`. Use `UTF8Encoding($false)`.
- **The trust gate is real, blocking, and visible.** A new or changed hook produces a
  full-screen TUI startup prompt — *"Hooks need review / N hooks are new or changed /
  1. Review hooks  2. Trust all and continue  3. Continue without trusting (hooks won't
  run)"* — **before the session starts**. So a Codex card's first launch parks on that
  screen, and it returns whenever ACT's hook definitions change. Two pre-session gates
  now, stacked with the directory-trust prompt.
- **Trust is stored in the user's own `config.toml`**, keyed positionally, hashed per
  handler — so ACT cannot avoid the user's config being written to (Codex does it, not
  ACT), and the hash confirms the byte-stability requirement:

      [hooks.state.'C:\Users\<u>\.codex\hooks.json:session_start:0:0']
      trusted_hash = "sha256:8325e47c…"

- **Escape hatch exists:** `--dangerously-bypass-hook-trust` / `-c bypass_hook_trust=true`
  ("Enabled hooks may run without review for this invocation"). Skips the review screen.
- ⚠️ **Hooks never executed, under any combination tried.** Discovered and parsed, yes;
  run, no. Tried: `SessionStart` with `matcher` `"*"` and `"startup"`, plus
  `UserPromptSubmit`; untrusted, trusted (approved via the review screen), and
  trust-bypassed; `codex exec` and the interactive TUI in a real console; a trusted
  working directory; and a hook command reduced to a **single-token `.bat` path** so no
  shell parsing, quoting, or redirection could be at fault. No output file, and no hook
  entries in the session rollout `.jsonl`. Consistent with the open upstream issue
  (openai/codex#17532, hooks not firing) — so this reads as a CLI defect, not a
  misconfiguration. `codex exec` additionally appears to skip hooks entirely.
- **Consequence for step 8:** the *original* question — whether a `command` hook expands
  `$VAR` or needs a shell to read the inherited env — **could not be answered**, because
  nothing ever ran. Step 8's Codex ingestion must therefore start from **files + process
  signals** (the `sessions/*.jsonl` rollout and `session_index.jsonl` already carry the
  session id, which is what the `SessionStart` payload was wanted for), and treat hooks as
  an upgrade to switch on once a CLI build actually fires them. Re-test with:
  a BOM-free `$CODEX_HOME/hooks.json`, `-c bypass_hook_trust=true`, and a single-token
  `.bat` — if the file appears, hooks are back.
- **Useful side-findings.** The Store-packaged `codex.exe` under `WindowsApps` **cannot be
  executed** by a normal process (access denied) and is not on `PATH`; the runnable copy is
  at the `CODEX_CLI_PATH` recorded in `~/.codex/config.toml`
  (`%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`) — `ExecutableResolver` will not find
  `codex` by name on a desktop-app-only install. There is also an app-server JSON-RPC
  method **`hooks/list`** (seen in `logs_2.sqlite`) which would be the clean way to inspect
  hook state, if the `initialize` handshake shape can be worked out.
- **`codex debug models` no longer matches the table above** — `gpt-5.6-sol` is now listed
  (efforts low…ultra, default low), and the account rejected `gpt-5.4`, `gpt-5.6-sol` and
  `gpt-5.4-mini` with *"not supported when using Codex with a ChatGPT account"*; `gpt-5.5`
  worked. Treat the effort table as a snapshot, and resolve models at runtime.

## Go-public checklist (when ready)

Independent of the build phases — do whenever you decide to flip the repo public.

- [ ] **No secrets in history** — the whole commit history becomes visible on
  flip. From day one: `.gitignore` for `.env`/keys, use GitHub Actions secrets,
  never commit the per-session endpoint token or any API key. (Scrubbing history
  later is painful — prevent, don't fix.)
- [ ] **License in place from the first commit** — `LICENSE.md` (FSL) with your
  name + change date committed early, so there's no ambiguous unlicensed period.
- [ ] **Third-party NOTICES file** present (permissive-dependency attribution).
- [ ] **Flip visibility** — repo Settings → change visibility → public.
- [ ] **Enable the full CI matrix** — turn on Windows + Linux on every push/PR
  (free and uncapped once public).

## Deferred — multi-OS release builds

`release.yml` currently packages **Windows only** (`win-x64` portable `.exe`).
Electron artifacts are per-OS — a Windows build does not run on Linux/macOS — so
shipping cross-platform means a build per target:

- [ ] **Linux** — `linux-x64` (`.tar.xz`, already configured in
  `electron-builder.json`). Cheapest to add: another matrix leg on
  `ubuntu-latest` running `package-desktop.ps1 -Rid linux-x64`.
- [ ] **macOS** — `osx-arm64`/`osx-x64`. Most work: needs a `mac` target added,
  must build on a `macos-latest` runner, and wants Apple signing + notarization
  to run without Gatekeeper warnings.

Note the CI-cost angle while private: macOS runners bill at 10× minutes, Windows
at 2× — a reason to defer the Linux/macOS legs until actually needed or until
go-public. (ACT's stated floor is "Windows + Linux at least.")
