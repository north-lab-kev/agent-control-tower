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

- [x] **4. Static board** — five flat columns, flight strips, compact/spacious
  toggle, rendering seeded cards. Pure UI, no behavior. *Verify:* board reflects
  store; density toggle works. ✅ Columns with live counts, flight strips
  covering all eight rail states, both densities rendering from the LiteDB store
  via `ICardStore`. Density is chosen in the settings dialog (no top-bar toggle)
  and survives a restart. Compact shows the **badge**, not a dot, per the spec —
  the title ellipsizes to give the badge its width, verified down to the 210px
  minimum column with no overflow and no clash with the `!` attention corner.
  - [x] **Needs feedback + To review merged into Your turn** — decided and landed
    2026-07-30, so the board is five columns, not six. The two said the same
    operational thing and differed only in the reason, which the badge already
    carried; `idle` became the **`to review`** badge, sign-off is gated by the
    column rather than the badge (a crashed or killed card can now be signed off
    without a terminal round trip), and `Act.Core/Rules/AttentionOrder` gives the
    merged column the priority its two neighbours used to imply by adjacency. The
    attention blink now covers the whole column in three colours — amber blocked,
    red error/killed, review blue for `to review`. See the spec's *Why one column
    and not two*. **`docs/ui-preview.html` still shows the old six** — it is a
    frozen mockup, and the spec wins.
- [x] **5. Task creation + manual moves** — new-task modal → card in Preparing;
  drag Preparing ↔ Ready with drag-time graying of invalid columns. *Verify:*
  can hand-manage cards through the human-controlled columns. ✅ Cards can be
  created, reopened for editing, and dragged between the two human columns, with
  every change persisted.
  - [x] **Creation** — new-task modal with the full control set (title, prompt,
    dir, agent, model, effort, permission mode, schedule; tools / flags / env
    behind *Advanced*), required-field validation,
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
    `Act.Core/Rules/ManualMove` owns the validity rules (unit-tested across every
    column pair); only human-controlled cards carry `draggable`, machine cards
    do not lift at all. On pick-up the valid target lights and every invalid
    column grays to 40%; the source column stays neutral. A drop appends a
    `Transition` and persists via `ICardStore.UpdateAsync`.
    `Ready → Executing` (launch, step 7) and `Completed → Your turn`
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
  - **Revised again 2026-07-30 — the `.act/` file contract is gone.** What this step
    shipped as the agent↔ACT contract (`ActContract`, `AgentPreamble`, and the
    `Preamble` field on both launch requests) is being deleted: the status file went
    with auto-completion, and follow-ups become an MCP tool at step 12. The seam is
    otherwise unchanged — this removes a convention, not a port. Step 8 item 3 does
    the deletion; the reasoning is in the spec's *Agent ↔ ACT contract* and *There is
    no completion signal*.
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
        emits `Stop` and still lands in Your turn. Checking the *transcript* for the task text is
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
- [x] **8. Ingestion — observability only** — normalized event stream fed per
  adapter (Claude: http hooks + transcript + process; **Codex: hooks + rollout + process** —
  its hooks were thought dead and turned out to be ACT's own quoting bug, fixed and verified
  2026-07-31, see *Codex hook findings*), over a localhost
  hook endpoint with a **per-session token**.
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
  - **To finalize step 8 — seven items, in this order.** Two of them are decisions that
    change where later code lives, so they come first.
    - [x] **1. `IIngestionSource` is deleted — ingestion is push.** ✅ Decided the other way
      from the recommendation that stood here: the port had zero implementations because
      nothing about ingestion is a *pull*. A hook post, a file change and a dying process are
      all things that happen *to* ACT, and each already had a way in — `IAgentEventSink` →
      `SessionEventSink` → `PtyAgentSession.Publish`. Making the transcript tailer implement a
      second seam would have bought an interface, an owner for its lifetime, and a drain loop
      per source, to deliver events the existing sink already carries. **So the transcript
      tailer (item 4) and the Codex rollout tail (items 6–7) are publishers**: they normalize
      what they read and push, exactly as the hook endpoint does. The stale comments that
      claimed the adapter "composes ingestion sources" and hands them in are gone from
      `IAgentAdapter` and `PtyAgentSession`, and the spec's *Pluggable, multi-source
      ingestion* now says a source is a publisher, not a port.
    - [x] **2. `TurnOutcome` is collapsed.** ✅ The enum and both `TurnEnded` fields
      (`Outcome`, `Question`) are gone — `TurnEnded` is now just session + timestamp, and a
      `Stop` always routes Executing → Your turn with `to review`. `TransitionReason` lost
      `TurnNeedsInput` and `TurnWithoutStatusFile` and `TurnReadyForReview` became
      **`TurnEnded`**, one reason for the one thing that can happen, with its two resx entries
      per language deleted. The two tests that asserted the outcome routing were replaced by
      one that asserts review, plus a new one pinning the case worth being explicit about: a
      turn that ends while the card is blocked **still** goes to review, because the block is
      over (the prompt was denied and the agent stopped) and the alternative is a card waiting
      on a prompt nobody will answer.
      - **Retiring a stored code needed the mapper to tolerate one.** `TransitionReason` is
        persisted by name, and LiteDB's enum deserializer throws on a name this build no
        longer has — so deleting two reasons would have made every existing card that carried
        one unreadable, on a real `act.db`. `ActBsonMapper` now registers a custom
        `TransitionReason` converter that reads an unknown name back as **null**, which
        `TransitionText.For` already renders as the transition's verbatim note. Regression
        test in `CardStoreTests` writes `TurnWithoutStatusFile` into the raw document and
        reads the card back. Cost: timeline rows written by an older build show their note
        instead of a sentence.
    - [x] **3. Delete the preamble and the `.act/` contract.** ✅ Landed. `AgentPreamble`,
      `ActContract` and their two test classes are gone, `Preamble` is off both launch
      requests, and each adapter passes `request.InitialPrompt` verbatim — so **the opening
      prompt is now the user's task text alone**, removing the largest and most quote-dense
      part of the launch argument. `autoComplete` / `AutoGitOptions` / `GitAction` went with
      it (card model, `CardDuplicate`, `NewTaskForm`, the task page's Automation fieldset,
      the flight-strip chip, ten resx entries across both languages, one store fixture).
      **`autoGit` was then brought back on its own**, re-cut as a prompt suffix
      (`Act.Core/Agents/AutoGitInstruction`) with no dependency on completion: the task form
      offers the git action ungated, `SessionLauncher` appends one sentence at launch, and
      `Card.InitialPrompt` still stores the user's text verbatim.
      - **And it established that the core owns resources.** Text ACT sends to an *agent* is
        localised too — the agent is addressed in the user's UI language — so
        `Act.Core/Resources/CoreStrings.resx` (+ `.fr`) exists, generated by the same
        source generator the app uses. Verified: the French wording resolves in tests, and
        `Act.Core.resources.dll` lands in the app's output beside the app's own.
      The two adapter regression guards were rewritten from "carries the preamble" to
      **"is the task text alone"**, which is the stronger assertion. 332 tests green; the
      task form and board verified in a real browser with no console errors.
    - [x] **4. Transcript source — the missing producer, and the biggest item.** ✅ Landed for
      Claude Code, and `SessionEnriched` now has a producer, so `MetricsProjection.Merge` is
      reachable and a card in Executing reports how it is doing rather than only that it is
      alive. **Verified live:** a launched card walked trust-prompt → running → `to review`
      and its strip finished on `claude-sonnet-5 · 55k/1000k ctx · 1 turns · 39k tokens`,
      where every one of those numbers was permanently blank before.
      - **Shape: a publisher, per item 1.** Core port `ITranscriptReader` (+`TranscriptRead`)
        with `TranscriptReader` in `Act.Infrastructure/Transcripts`; core port
        `ITranscriptNormalizer` implemented per adapter, so the line dialect sits beside the
        hook dialect; `Act.Core/Agents/TranscriptTail` holds the offset and the running
        snapshot; `Act.App/Sessions/TranscriptPump` owns one polling loop per live session and
        publishes through `IAgentEventSink`. The path arrives as
        `HookNormalization.TranscriptPath` → `IAgentEventSink.LocateTranscript` →
        `SessionRegistry.LocateTranscript`, which raises once per session (`TryAdd` is the
        whole guard) — mirroring how `SessionId` already reaches `Bind`.
      - **Polled once a second, full read first, incremental after.** The first read takes the
        whole file, so a restart or a resume does not report an hour-old session as new; every
        read after it starts where the last stopped. The reader hands back that offset rather
        than the file's length, because a line the agent is still writing has to be left for
        the next read — and a file that *shrank* was replaced, so the read starts over.
      - **Tokens and context answer different questions from the same usage block.** Tokens are
        cumulative and count fresh work only (`input_tokens` + `cache_creation`); cache reads
        are excluded, because a long session re-reads the same cached prefix every request and
        summing those reaches tens of millions. Context is the *last* request's total with
        cache reads included, and it overwrites rather than accumulates — which is what makes
        it fall after a compaction.
      - **The context window comes from `AgentModel.ContextLimit`**, matched against the
        *observed* model (a transcript says `claude-opus-5`, a launch asked for `opus`), and an
        unknown model shows no percentage rather than a wrong one. The numbers were **read out
        of the CLI's own model table** in `claude.exe` 2.1.220: `context: { window }` is 1e6 for
        `claude-opus-5` / `claude-sonnet-5` / `claude-fable-5` and 200000 for
        `claude-haiku-4-5`. Two things shrink the CLI's *effective* window invisibly to ACT —
        `CLAUDE_CODE_MAX_CONTEXT_TOKENS`, and a session whose 1M credits are blocked falling
        back to 200k — and both make ACT read low, never high.
      - **`cost` is gone from the model** (`CardMetrics.Cost`, the strip chip, one store
        fixture): the transcript carries no cost, computing it needs a price table that drifts
        silently, and it is notional on a subscription anyway. The chip it vacated now shows
        **cumulative tokens**, which has a producer and is not a guess.
      - **Publishing is change-gated.** `TranscriptTail.Advance` returns null unless the folded
        snapshot differs, because `BoardState.UpdateAsync` re-reads every card — an identical
        snapshot would re-render the whole board once a second for news that is not news.
    - [x] **5. Idle watchdog — not built. `stale` is deleted and the chip states the gap.**
      ✅ The watchdog was designed, then the premise was challenged and did not survive: every
      block on the user already moves the card out of Executing, so what is left to detect is
      unknowable from silence. Measured first — 2,729 within-turn gaps in real transcripts give
      p50 1.3s, p99 45s, and **every** gap past three minutes was a session waiting on its
      human. A model that stops answering is not something this repo has evidence for.
      - **What shipped instead:** `Act.Core/Rules/QuietSession` (pure predicate, Executing only,
        15-minute threshold ≈ 20× the p99) and a `quiet 22m` chip rendered **beside** the badge,
        which keeps saying `running`. No event, no transition, no state — ACT states the gap and
        claims nothing about its cause. The full enumeration of what silence can mean (Codex
        having no prompt signal at all, a block *inside* a tool call, a mid-turn rate limit, auth
        expiry, ACT having lost its own ingestion, the pty layer) is in the spec's *No stale badge
        — the quiet chip instead*.
      - **Deleted:** `NoActivityElapsed`, `Badge.Stale`, `TransitionReason.NoActivity`, three
        resx entries per language, the `b-stale`/`r-stale` CSS across three components, and the
        rules-engine rule. `--act-stale` became `--act-quiet`.
      - **Two costs, both accepted.** *No history* — a watchdog would have written a transition,
        so a morning-after timeline could say "went quiet at 03:12"; the chip only shows the
        current gap. And *a refresh tick* — `BoardView` re-renders every 30s while any card is in
        Executing, because a number derived from a stamp has nothing to push a render. It is
        presentation only, and idle when nothing is running.
      - **A retired badge had to stay loadable.** A real board had cards carrying `Stale`, so
        `ActBsonMapper` now maps `Badge` the way it already maps `TransitionReason`: a name this
        build does not know reads back as null instead of throwing. Verified against a card
        rewritten to `"Stale"` in the raw document — and live, where the seeded `#1040` came back
        badge-less and intact, with `quiet 52h` beside it.
    - [~] **6 + 7. Codex binding, activity and enrichment from the rollout — code complete, live
      verify blocked.** ✅ Written and unit-tested against **real rollout files**, ⚠️ but not proven
      end to end, because Codex's TUI paints **nothing** under ACT's pseudo-terminal (see the
      blocker below). What landed:
      - **`CodexRolloutFinder`** (`ITranscriptFinder`, a new core port) infers the file ACT's launch
        just made: newest few `rollout-*.jsonl` under `$CODEX_HOME/sessions`, oldest-first, matched
        on the `session_meta` line's `cwd` and timestamp, skipping any path another live card holds.
        **This is the answer to "how do we bind without hooks":** that first line carries
        `session_id`, so the file that identifies the session also *is* the binding. The whole class
        is marked to be **deleted when Codex hooks fire** — a payload would simply say where the
        file is, as Claude Code's already does.
      - **`CodexTranscriptNormalizer`** folds the dialect, measured 2026-07-31: `task_started` →
        activity, `task_complete` → turn end (or `TurnFailed` when it carries an `error`),
        `function_call` → activity with the tool name, `token_count` → tokens, context **and
        `model_context_window`** — so Codex needs no per-model window table, unlike Claude Code.
      - **`ITranscriptNormalizer` now returns events as well as enrichment**, because Codex's file
        has to do the work its hooks would. `TranscriptTail` **drops the events of the catch-up
        read** and keeps only its snapshot: replaying an hour of history would move the card on a
        turn that ended long ago and count every turn twice.
      - **New event `TurnFailed`** — the CLI reporting a failed turn while its process stays alive
        and would exit zero (measured: an account rejecting `gpt-5.4`). `ProcessExited` cannot see
        it, and a bare `TurnEnded` would offer an api error up for review as if it were work.
      - ✅ **"Codex's TUI does not paint" was wrong — measured 2026-07-31 and withdrawn.** Spawned
        through ACT's own `PtyHost`, Codex paints in **93 ms**: 644 characters, its directory-trust
        screen legible in the captured stream. It also **accepts ACT's keystrokes** — writing `\r`
        through `IPtyProcess.WriteAsync` answered the prompt, the session started and a rollout
        appeared. Both halves of the pty contract hold.
        - **What was actually broken: the preview pane.** It reports `document.hidden`, and xterm in
          a hidden, unfocused document neither renders (22 empty row divs) nor delivers input — so
          "empty terminal, keystroke ignored" was the harness, not ACT and not Codex. Rendering was
          already recorded as a pane limitation; **input is the new half of that finding**. Verifying
          anything terminal-shaped needs a real window.
      - ⚠️ **The real blocker, and it is a consequence of restoring `--profile`: a second pre-session
        gate.** With ACT declaring hooks, every Codex launch parks on a full-screen
        *"Hooks need review / 9 hooks are new or changed / 1. Review hooks · 2. Trust all and
        continue · 3. Continue without trusting"* — **before** the session exists, so no rollout, no
        binding, nothing to ingest. And **`-c bypass_hook_trust=true` does not skip it** on this
        build, though it is the documented escape hatch. It was a decision rather than a fix, and it
        was taken on 2026-07-31 — **answer the review once and keep the declarations**; see item 8
        below for the price and the alternative that was rejected.
      - ✅ **A real mis-binding bug, found by the live run and fixed.** Card #1082 bound itself to a
        session from 30 minutes earlier and reported *its* tokens and turns. Cause:
        `SessionLauncher` does `card.LaunchedAt ??= clock.Now`, so on a relaunch that stamp is
        arbitrarily stale and every rollout written since passed the time filter. The search now takes
        **this session's spawn** (`clock.Now` as the pump starts looking) and, when the card already
        has a session id — a restore — matches the rollout **by identity** rather than guessing at
        all. Four tests, including the exact scenario that bit.
      - ✅ **And a launch-killing bug found and fixed on the way in — the hooks schema moved.** ACT's
        generated `--profile` carried `hooks = "<path>"`, which this CLI **rejects outright**:
        *"Error loading config.toml: invalid type: string … expected struct HooksToml"*. Every Codex
        launch ever made died on exit code 1 with an empty terminal. That directly contradicts the
        earlier finding below — a string path was what a `[[hooks.*]]` table was rejected *in favour
        of* — under the same version string.
        - **The real shape, measured rather than guessed:** the profile's `hooks` is a table keyed by
          event name, each a list of matcher groups —
          `[[hooks.<Event>]]` with `matcher`, then `[[hooks.<Event>.hooks]]` with
          `type` + `command`. Handler types are `command`, `prompt`, `agent`. `hooks.state` is the
          map Codex writes trust hashes into.
        - **How it was pinned down, and the trap in doing so:** the parser **ignores unknown keys**,
          so a wrong shape parses in silence and buys nothing. The way through is to give a candidate
          key the wrong *type* and let serde name what it wanted: `hooks.state = 1` → "expected a
          map"; `hooks.SessionStart = 1` → "expected a sequence"; `matcher = 1` → "expected a
          string"; `type = "bogus"` → "unknown variant `bogus`, expected one of `command`, `prompt`,
          `agent`". `hooks.hooks` / `files` / `path` / `description` were all ignored, so none exist.
          The probe harness: write the candidate to `act.config.toml`, run `codex --profile act x`,
          and read the first line — a config error means wrong, *"stdin is not a terminal"* means the
          config parsed and the CLI got as far as wanting a terminal.
        - **A second bug the same run exposed:** the composer escaped `\` but not `"`, and the command
          value quotes the forwarder path so a path with spaces survives — so the generated file was
          not valid TOML at all. `Toml()` now escapes both.
        - **Verified three ways:** the schema type-probed against the CLI, the CLI accepting ACT's
          *own generated bytes*, and a live ACT launch where the Codex process stays alive with
          `--profile act` instead of dying on exit 1. Four tests pin the shape, including that the
          profile and the json declare a byte-identical command — they are hashed for trust, so a
          drift between them would cost a second review prompt.
      - **Also needed to get this far: the task form can now set the agent executable.**
        `LaunchConfig.AgentBinary` existed and nothing could ever set it, so a Store-installed Codex —
        not on `PATH`, runnable only from the `CODEX_CLI_PATH` in `~/.codex/config.toml` — could not
        be launched at all. One field under *Advanced*, both languages.
    - [x] **8. What ACT declares in the Codex profile — decided 2026-07-31: it keeps declaring them,
      and the review is answered once.** The pty is fine and the ingestion code is written; what stops
      a Codex card is the hook-review gate ACT's own declarations raise, which the documented bypass
      does not skip. The alternative considered and rejected was writing the profile *without* hook
      declarations until hooks fire — cheaper today, but it trades away the day-one readiness that
      `PermissionRequest` (the only real permission signal either agent could ever have) is worth.
      **So there is no code change here: `CodexHookConfig.ComposeProfile` stays as it is, all nine
      handlers declared, and the gate is answered in the terminal like every other prompt.**
      - **What answering it costs, so it is a known price and not a surprise.** Pressing *2. Trust all
        and continue* makes Codex write nine `trusted_hash` entries into the **user's own**
        `~/.codex/config.toml` under `[hooks.state.…]`, and grants ACT's forwarder permission to run
        outside Codex's sandbox. The hash is keyed on file path + event + handler index, so it is
        *once per definition change*, not once ever — changing the event list or moving the forwarder
        re-raises the review. That is why the endpoint url and the per-session token ride the process
        environment and never the command string.
      - **And the card must not read the gate as a hang.** It is a second pre-session gate stacked on
        directory trust, and the startup-grace path (`StartupPromptWaiting` → Your turn,
        `needs permission`) is what has to cover it.
      - ✅ **Verified live 2026-07-31 in a real Edge window, cards #1083–#1088.** A Codex card reaches
        its TUI, **binds its session id from the rollout** (`019fbac2-…` on the rail), and reports
        live state and metrics — `to review` on `task_complete`, `13k/258k` context, 1 turn. A
        relaunch reaches the TUI with **no gate at all**. Three bugs had to be fixed to get there,
        all found by this run and none visible to the unit suite:
        - **Codex writes its trust hashes into ACT's own generated profile**, not the user's
          `config.toml` as the findings doc claimed — so ACT's unconditional rewrite destroyed the
          review the user had just answered and the nine-hook gate came back every launch. Now
          `IAgentConfigFiles.WriteExternalPreservingTail` carries everything from the first
          `[hooks.state` line across. **Comparing the file with ACT's own bytes is not enough** and
          that first attempt failed live: Codex reformats ACT's block when it saves.
        - **The transcript search window expired while the gate was still up.** It ran one minute
          from spawn, and the rollout only appears when the *session* starts — 2m43s later on the
          measured run — so a card ran a whole turn unbound and reported nothing. The search now runs
          for as long as the session is live, with no deadline.
        - **`sink.Bind` never reached the card.** It set the id on the live session object only, so
          `card.SessionId` stayed null: the rail read "reported at session start" forever and
          `CodexRolloutFinder.ByIdentity` — the restore path — could never fire. `TranscriptPump`
          now persists it. Not unit-tested: nothing covers the pump, which is the stateful app-level
          loop the testing standard deliberately leaves to live verification.
      - ✅ **And the run reversed the premise of this whole item: Codex hooks fire, and ACT's
        ingestion through them now works.** They fire on the same `0.146.0-alpha.3.1` the findings doc
        measured as never running them. Every one failed at first with `hook exited with code 1` — and
        the cause was **ACT's own quoting**, not the CLI: Codex does not strip quotes when it resolves
        the program, so `"<forwarder>" <Event>` named a program called `"C:\…"`. ACT had emitted that
        since its first Codex launch, which is why no payload had ever arrived. Fixed to
        `<cmd.exe unquoted> /c "<forwarder>" <Event>` and verified live on card #1091:
        `SessionStarted`, `ActivityObserved` ×3, `TurnEnded` normalized from real payloads, no red
        blocks in the terminal, and the timeline carrying **one** turn-end row even though the rollout
        reports it too. Details and the measurement tables are in *Codex hook findings*.
      - ~~**The gap to accept when it does work.**~~ **Closed 2026-07-31** — this said a Codex card
        would never badge `needs permission`, because no file or process signal reports a waiting
        prompt. True until the hooks ran: `PermissionRequest` now delivers exactly that (item 9). The
        quiet chip keeps its other justifications; this is no longer one of them.
      - **And a divergence to undo, not a design.** Claude Code leaves `TurnCount` and `ToolCalls`
        null because its hooks count both first-hand; Codex takes them from the rollout only because
        its hooks do not fire. **The moment a CLI build fires them, align Codex with Claude Code** —
        hooks own the counts, the transcript owns enrichment. The note is on `ITranscriptNormalizer`
        too, where whoever revisits the fold will see it.
    - [x] **9. The hook exec failure — chased and fixed 2026-07-31. Codex is no longer the
      lesser-instrumented agent.** The rule, measured across two probe rounds of five candidate shapes
      each (tables in *Codex hook findings*): **the program token must be unquoted, and everything
      after it may be quoted.** Not the `.cmd` extension, not arguments, not the shell, not the
      sandbox — only the quotes. Shipped as `<cmd.exe unquoted> /c "<forwarder>" <Event>`, which also
      survives a space in the path because `cmd` parses that part.
      - ✅ **`PermissionRequest` works, so the "gap to accept" above is closed.** Card #1092: an
        `apply_patch` approval raised `PermissionRequested` → **`needs permission`**, from a prompt ACT
        never touched. Codex now has the one signal Claude Code has to infer from a notification
        message — and it arrives as an explicit event rather than a string to classify.
      - **Two follow-ups this opens, neither urgent and neither done.** Payloads carry `session_id`
        *and* `transcript_path`, so `CodexRolloutFinder` — written explicitly to stand in for a hook
        that never fired, and marked for deletion when one did — can be retired; and the
        `TurnCount`/`ToolCalls` divergence noted above is now the thing to align, since hooks can own
        the counts and the transcript the enrichment. Both need their own live verify, so they are
        left as work rather than folded into a verified step.
      - **The probe technique, worth reusing.** Codex prints no detail beyond the exit code and logs
        no hook records anywhere, so there is nothing to read — only experiments. Giving each event a
        differently-named probe that logs its own name turns a yes/no into a table for the price of
        one trust prompt (the review screen counts only the definitions that changed).
  - **Verify (revised):** for **Claude Code** — live state and metrics update, and a
    permission prompt in the terminal raises the badge without ACT touching the prompt
    (**all observed live**, and since item 4 the strip carries the observed model, context
    against the real window, turns and tokens). For **Codex** — the card binds its session id
    from the rollout file, and shows live state and metrics; no permission/question badge, by
    measured CLI limitation. ✅ **Codex's half is now verified live too** — 2026-07-31, card #1088:
    session id bound from the rollout, `to review`, context and turns on the rail, no gate on
    relaunch. What remains open is not ingestion but item 9: whether ACT keeps declaring hooks that
    fire and then fail.
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
      permission prompt badged a reviewable card `needs permission`, with nothing to approve, and
      overwrote its `to review`. An unrecognised notification now produces **no event** —
      calling it activity would have been worse still, since an idle nudge means the opposite
      of activity and would send a reviewed card back to Executing by itself.
    - ✅ **A real permission prompt reports as `Claude needs your permission`** — observed
      2026-07-30, once the prompt-truncation fix let an agent actually start work and hit a write
      gate. The card moved to Your turn with `needs permission`, from a prompt ACT never
      touched. Both halves of the `Notification` question are now measured rather than assumed.
    - ✅ **`Notification` carries `notification_type`, and the message never had to be
      guessed at** — captured 2026-07-30 by logging raw payloads at the endpoint on
      `claude-code v2.1.220`: `permission_prompt` for a waiting prompt, `idle_prompt` for the
      nudge. The classifier keys on that field now, and keeps the "permission" / "approve"
      substring check only as a fallback for a payload without it.
    - ✅ **`AskUserQuestion` is announced as a permission prompt, and only `PreToolUse` knows
      better** — measured the same way. Its `Notification` is byte-for-byte a `Bash`
      approval's, so **a question to the user was badged `needs permission`**. The fix keys on
      `tool_name` — that one `PreToolUse` normalizes to a question and carries the text from
      `tool_input.questions[]`, which no notification has — and the rules engine drops the
      permission notice that follows, since it describes the same block and says less. Verified
      live: an `AskUserQuestion` card lands on `needs answer`, a `Bash` approval on
      `needs permission`.
    - ✅ **Codex hooks — they fire, and ACT ingests them.** The 2026-07-29 measurement said they
      never ran at all; on 2026-07-31, on the same version string, they fire at the right moments,
      and the `exited with code 1` that followed was ACT quoting the program token. Fixed and
      verified live (items 8–9). So both agents now report through hooks, `PermissionRequest`
      included — the file sources remain, but as enrichment rather than the only way in. See
      *Codex hook findings*.
- [x] **9. Rules engine** — agent-agnostic: normalized events → column/badge
  transitions; a turn end (`Stop`) routes Executing → Your turn with `to review`;
  `error` from a
  process signal; **observed** permission/question in, and **observed activity**
  (`UserPromptSubmit`) as the one way out of Your turn — the recovery and the
  send-back are the same event, since the user acts in the terminal and ACT only
  ever learns about it.
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
    Verified live: a launched card walked Ready → Executing (`running`) → Your turn
    (`to review`) on its own, with the turn count landing on the strip.
  - **Nothing left to finish — closed 2026-07-31.** The last open item was `stale`, and it was
    deleted rather than built (step 8 item 5): silence is not a state the engine can rule on.
    The `ready_for_review` / `needs_input` outcomes went with the status file (see the spec's
    *There is no completion signal*), and the `Notification` classification landed with step 8.
    So the engine's table is now complete as specified, and every rule in it has a producer.
- [ ] **10. Session view + card actions — both adapters** — the full session view
  (terminal + right rail: identity, cwd, agent/model, context %, tokens, turns;
  back-to-board) and the drawer's **read-only** blocked-card presentation with
  "Answer in terminal ›". Actions: Open terminal, **Open in Desktop**
  (`claude://resume?session=`), Kill, Retry, Send back, Complete. Explicitly **no
  approve/deny anywhere**. *Verify:* a task goes full-circle by hand on both agents,
  answering every prompt in the embedded terminal.

## Phase 4 — Completion & lineage

- [ ] **11. Complete + reopen** — Your turn → Completed and reopen. *Verify:* a
  signed-off task lands in Completed; reopen resumes.
  - **Auto-complete was cut on 2026-07-30** — see the spec's *No auto-completion*.
    Completion is always the user's drag, so this step is now just the two manual
    transitions, and nothing in ACT needs the agent to assert that it finished.
    **`autoGit` survived** and left this step entirely: it is a prompt suffix
    (`AutoGitInstruction`), applied at launch and unrelated to completion.
  - [x] **Your turn → Completed, brought forward.** `Act.Core/Rules/CardCompletion` is the
    predicate (its own rule, because reaching Completed stamps the sign-off and ends the session,
    which `BoardState.MoveAsync` neither does nor should — and because no event may decide it),
    and `Act.App/Sessions/CardCompleter` is the action, the mirror of `SessionLauncher`. It stamps
    and persists the card *before* tearing the terminal down, so the `SessionKilled` the dying
    pty reports lands on a card the engine no longer governs. The gate is the **column**, so
    every card in Your turn can be signed off, `error` and `killed` included.
    Verified live: a launched card walked to Your turn and was signed off from both surfaces —
    Completed, badge `done`, `Marked completed` on the timeline, agent process gone.
  - [x] **The sign-off is a drag, not a button.** A Your turn card now lifts (`ManualMove.CanDrag`)
    and Completed is its only legal drop (`CardCompletion.CanCompleteInto`), which `BoardView`
    routes to `CardCompleter` rather than to `MoveAsync`. The strip's Complete button is gone —
    the board has one gesture for moving a card — and the session view keeps its rail action for
    a card you are already inside.
  - **Still step 11's to finish:** reopen (Completed → Your turn), and the completion/git
    modal — completing is a plain action for now, with no prompt and no spawned git task.
- [ ] **12. Spawning & lineage — an MCP server** — the `create_followup` tool,
  parent/children, the git-on-completion spawned task. *Verify:* a real agent calls the
  tool mid-session and a tracked child card appears in Ready with correct lineage, on both
  agents.
  - **Decided 2026-07-30: one MCP tool, not a file and not a curl command.** The whole
    agent→ACT contract is this one tool; the `.act/followups/` file mechanism was dropped
    with the status file. Rationale (payload shape, grant width, sandbox avoidance,
    instruction decay, simpler `dependsOn`) is in the spec's *Agent ↔ ACT contract* — read
    it before building, it is the design.
  - [ ] **Host it on the kept hook port, streamable HTTP, at `/mcp`.** Not stdio: stdio
    means one child process per session, and ACT has already paid once for leaking a
    process per session (the `conhost` bug at step 7). The Kestrel listener, the kept port
    and the per-session token all exist — this is a third route family beside
    `/hooks/claude` and `/hooks/codex`, and it reuses `x-act-hook-token` and the same
    task-resolution path as `SessionEventSink`.
  - [ ] **One tool, one namespace.** `create_followup` under server name `act`, so the
    pre-allow entry reads `mcp__act__create_followup`. Arguments
    `{ title, prompt, cwd?, dependsOn? }`; returns the minted `number` and `id`.
    **No `parentId` argument** — the token identifies the calling task, so an agent
    cannot spawn onto another card. That is also why the config file carrying the token
    must be per launch, never shared.
  - [ ] **Config injection per adapter**, mirroring what the hook config already does:
    Claude via `--mcp-config <path>` (per-launch file, alongside `act-settings.json`) plus
    `permissions.allow: ["mcp__act__create_followup"]` in the generated `--settings` so
    there is no approval prompt; Codex via `mcp_servers` layered through ACT's `--profile`.
  - [ ] **Dependency: the C# MCP SDK** — expected MIT, so check the license and add it to
    `THIRD-PARTY-NOTICES.md` before it lands (same discipline as `Porta.Pty` / xterm).
    **Consider `ModelContextProtocol` v2.0 (in preview as of 2026-07-30)** rather than the
    1.x line: check what it changes for the streamable-HTTP server host and per-tool
    registration before committing, and note that taking a preview package means pinning an
    exact version and accepting API churn until it ships stable.
  - [ ] **Dead code to remove:** the `FollowUpsWritten` event, which has neither a producer
    nor a consumer and described the file mechanism.
  - **Open questions — each decides a fallback, settle before building:**
    - ⚠️ **Does Codex's `mcp_servers` support streamable HTTP *with custom headers*?** It
      was stdio-only historically. If not, the fallbacks are (a) an ACT-shipped stdio
      shim that forwards to the loopback endpoint, or (b) the token in the URL path
      rather than a header — which then must not be logged.
    - ⚠️ **Can a per-launch MCP config coexist with Codex's byte-stable profile?** The
      hook-trust hash covers hook definitions, not `mcp_servers`, so a per-launch profile
      *should* be safe — but that is inference from the hook findings, not a measurement.
      If it is not safe, the stdio shim above solves it too, since a fixed shim path is
      byte-stable and the token can ride the process env.
    - ⚠️ **Does an MCP tool call really escape both sandboxes?** The premise is that the
      CLI's own process makes the MCP connection, so Codex's `workspace-write` network
      block and Claude's bash sandbox never apply. Measure it — it is the main reason the
      tool was chosen over a `curl` command.
    - ⚠️ **Does Claude Code's MCP approval respect `permissions.allow` from `--settings`
      for an `mcp__*` tool id?** If not, the first follow-up per session costs an
      approval prompt, which is tolerable but should be a known cost rather than a
      surprise.
    - ⚠️ **Do either CLI surface an MCP server's `instructions` to the model?** Not
      load-bearing — the tool description carries everything needed, and ACT injects no
      preamble by design (see the spec's *ACT does not announce the tool*). It decides
      only where any future framing would live if it is ever wanted, so it is worth one
      look while the server is being built rather than a separate investigation later.

## Phase 5 — Automation (on a proven base)

- [ ] **13. Native notifications** — OS pings for attention states; actionable,
  focus-aware, coalesced. *Verify:* backgrounded ACT pings on needs-you.
- [ ] **14. Scheduling & queue runner** — `schedule`, `maxConcurrent`,
  `dependsOn` ordering, rate-limit backpressure (`waiting-reset`), keep-awake,
  and the global **auto-execution pause switch**. *Verify:* a queue of Ready
  tasks processes unattended across a window reset; pause halts all auto-launch.
  - **Keep-awake must become task-aware.** It ships unconditional — the hold is
    taken at startup for the life of the process whenever the setting is on. Here
    it must hold sleep **only while a Ready or Running task exists**, and release
    otherwise: an idle board is safe to sleep, since nothing can auto-launch from
    it. Scheduled work (*specific date & time*, *next window*, `waiting-reset`)
    sits in Ready, so a Ready/Running predicate still covers the overnight queue.
    This is what the spec's *Keep-awake* section already asks for ("only inhibit
    sleep when there's pending/active auto-work"); the Settings section's
    "as long as ACT runs" wording describes the interim behaviour and must be
    corrected when this lands.

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
  - ✅ **Both agents open with a directory-trust prompt** on a working directory they
    have not seen before ("Do you trust the contents of this directory?" /
    "Is this a project you created or one you trust?"), *before* the session starts.
    **Resolved:** measured on 2026-07-30 that *no* hook fires while it is up — not
    even `SessionStart` — against ~0.8 s to the first hook in a directory the CLI
    already trusts. So the process source reports the silence: no hook within an 8 s
    startup grace publishes `StartupPromptWaiting`, and the card goes to Your turn
    with `needs permission`. It is answered in the terminal like everything else, and
    the resulting activity brings the card back — but note it gates project-local
    config and hooks on Codex, so it is load-bearing for step 8.
- ✅ **`claude://resume?session=<uuid>` — verified** by the prototype against the
  desktop app's own `app.asar` (v1.24012.9.0): it validates a canonical UUID and
  calls `importCliSession`. Parameter is `session`, not `sessionId`; no `cwd` is
  read. Named failure modes: `transcript_missing`, `auth_expired`, `network`.
- ✅ **`/usage` scriptability + reset-time detection — resolved 2026-07-31, and the
  answer is that scripting it is unnecessary.** Neither CLI prints usage
  non-interactively, but **both expose a live HTTP endpoint** carrying percentages and
  reset times: `api.anthropic.com/api/oauth/usage` and
  `chatgpt.com/backend-api/wham/usage`, each on a bearer token read from the CLI's own
  credential file. ACT polls both and renders them in the top bar. Endpoints, response
  shapes, the three unit/encoding traps, and the rejected alternatives (statusline hook,
  pty scrape, transcript derivation, Codex rollout `rate_limits`) are in
  **`docs/agent-usage-findings.md`** — read it before touching usage code.
  - **Still open for step 14:** clean mid-task resume after a rate-limit interruption.
    The backpressure signals now have a source, though — Codex's `limit_reached` /
    `rate_limit_reached_type` and the window `resets_at` are what `waiting-reset` needs.
- ✅ **Codex runs under ConPTY — verified 2026-07-31 through ACT's own `PtyHost`.** It paints in
  93 ms (644 characters, its trust screen legible), and a `\r` written to the pty answers that
  screen and starts a session. So both directions of the pty contract hold for both agents, and
  `\r` is the submit key for `SubmitAsync`.
  - **What does *not* work is verifying it in the preview pane.** That pane reports
    `document.hidden`, and a hidden, unfocused xterm neither paints nor accepts keystrokes — which
    reads exactly like a broken CLI and cost a whole round of wrong conclusions. Anything
    terminal-shaped has to be checked in a real window, or at the pty layer directly.
- ✅ **Codex equivalents — resolved** against the installed CLI (`codex-cli
  0.146.0-alpha.3.1`) and the official docs; see *Codex facts* below.
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
- ⚠️ **The `hooks` key's schema moved, and it was killing every launch — now fixed.** Measured
  2026-07-31 on the *same* version string: a profile containing `hooks = "<path>"` makes the CLI
  refuse to start — *"Error loading config.toml: invalid type: string … expected struct
  HooksToml"* — the exact opposite of what the findings below recorded. The shape it wants now:

      [[hooks.<Event>]]
      matcher = "*"

      [[hooks.<Event>.hooks]]
      type = "command"          # or `prompt` / `agent`
      command = "…"             # one string; escape `\` and `"` for TOML

  `hooks.state` is the map Codex writes trust hashes into. **The parser ignores unknown keys**, so a
  wrong shape parses silently and does nothing — type-probe it (give a key the wrong type and read
  which type serde says it wanted) rather than trusting a clean start.
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
- ⚠️ **Re-tested 2026-07-31 with the corrected schema — still no execution.** The fix to ACT's
  profile raised a fair doubt about the finding below: it was measured with `hooks = "<path>"`, a
  config this CLI *rejects outright*, so "hooks never fire" could have been an artifact of a config
  that never loaded. It is not. With the **now-measured** `[[hooks.<Event>]]` shape, plus the
  auto-discovered `$CODEX_HOME/hooks.json` as a second independent route, plus
  `-c bypass_hook_trust=true` so the trust gate cannot silently skip a new definition, and on runs
  that genuinely completed a turn (`codex exec`, real model reply, tokens billed): the handler — a
  single-token `.cmd` that only appends to a file — **never ran**. Not once, either route.
  - **What that leaves untested:** the *interactive* path. `codex exec` was already suspected of
    skipping hooks entirely, and the TUI cannot be driven under ACT's pty because it paints nothing
    (see the pty blocker), so nothing there can be answered or observed. So the precise claim is:
    **no hook execution in `codex exec` on this build, via either config route, with trust
    bypassed** — and the interactive path is blocked rather than clean.
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
