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
    without a terminal round trip), and the merged column took a badge ranking
    (`AttentionOrder`) in place of the priority its two neighbours used to imply by
    adjacency — **retired 2026-08-02** for `CardOrder`, the user's own ordering. The
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
    only text ACT types itself — step 7 cut that down to the send-back message alone,
    and **step 10 deleted `SubmitAsync` outright** when send-back itself was cut).
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
      `IAgentTerminal.SubmitAsync` stayed for the send-back — and was deleted with it at
      step 10, so ACT now types nothing at all. Regression-guarded in
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
    - [x] **6 + 7. Codex binding, activity and enrichment from the rollout.** ✅ Written and unit-tested
      against **real rollout files**, then verified live — and then **largely deleted again** at item 9,
      once the hooks turned out to work: binding, activity and the counts all moved to payloads, and
      only the enrichment fold survives. Read this as the history of how Codex was instrumented before
      that, because two of its findings still hold (the rollout dialect, and `TurnFailed`):
      - **`CodexRolloutFinder`** (`ITranscriptFinder`, a new core port) — **deleted 2026-07-31, see
        item 9.** It inferred the file ACT's launch
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
      - ✅ **A divergence to undo, and it was undone.** Codex took `TurnCount` and `ToolCalls` from the
        rollout only because its hooks were believed dead; both agents now leave them to the hooks,
        which count first-hand. Landed with item 9, and the note is off `ITranscriptNormalizer`.
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
      - ✅ **`needs answer` works on Codex too — verified live 2026-07-31, card #1097.** Its
        ask-the-user tool is **`request_user_input`**; `CodexHookNormalizer` keys `PreToolUse` on it and
        raises `QuestionAsked`, mirroring `AskUserQuestion`. Verified by the one reachable path: `/plan`
        typed into ACT's own terminal, then an ambiguous request — the card badged **`needs answer`**
        carrying both questions, from a prompt ACT never touched. The text comes from a **`questions`
        array**, each entry with a `question` field (`prompt`/`question` kept as fallbacks).
        The tool is offered only in Codex's **Plan collaboration mode**: asked to use it in Default mode
        the CLI answers *"I can't use request_user_input in the current Default mode"*, asks in prose and
        ends the turn — which lands on `to review`, honestly. That mode is **not a config key**
        (`collaboration_mode`, `mode`, `collaboration` all type-probed 2026-07-31; none exist), so ACT
        cannot select it at launch. Note ACT's `PermissionMode.Plan` is a *different axis* — it maps to
        `--ask-for-approval never --sandbox read-only`, not to the collaboration mode.
        - **And ACT adds nothing for it — decided 2026-07-31.** Plan mode is the TUI's `/plan` command
          (confirmed in `codex.exe`'s strings, alongside `<proposed_plan>` and *"Continue planning with
          the model"*), so there is nothing a launch flag or a task-form field could set. Forcing it by
          default was considered and rejected outright: Plan mode **proposes** work rather than doing
          it, which would make every Codex card a plan and defeat the unattended queue of step 14. The
          classification stays because it is cheap and not dead code — a user can type `/plan` in ACT's
          own terminal, and the card then badges correctly. The badge is the weaker half anyway: a
          prose question ends the turn, so the card is already in Your turn; `needs answer` versus
          `to review` differs in the *reason*, not the action — the same argument that merged
          "Needs feedback" into "To review".
      - ✅ **A defect the same run exposed, found and fixed: a blocked card could flip back to
        `running`.** Card #1097 showed `running` while a Bash approval was still on screen — arrival
        order was `PermissionRequested` → `ActivityObserved`, and the activity moved the card out of Your
        turn before anyone answered. Cause: **`UserPromptSubmit` and the tool events normalize to the
        same `ActivityObserved`**, so the engine could not tell a user's keystroke from a tool running,
        even though step 9's "one way out of Your turn" means the former. Either Codex emits
        `PostToolUse` for the previous tool after the next one's `PermissionRequest`, or the posts race —
        the forwarder spawns one `curl` per hook, so **ACT sees arrival order, not emission order**; the
        rule holds for both. Fixed in `RulesEngine`: a tool-bearing `ActivityObserved` no longer clears
        `needs permission` (`ToolName` is null exactly for `UserPromptSubmit`, so no new signal was
        needed).
        - **Cost accepted, deliberately:** approving fires no `UserPromptSubmit`, so an approved card
          keeps saying `needs permission` until the turn ends. A stale "you are needed" costs a glance; a
          stale `running` hides a session waiting on a human, which is what the board exists to prevent.
        - **Permissions only, and the asymmetry is load-bearing.** A question is raised by its tool's
          `PreToolUse` and *answered* at its `PostToolUse`, so for `needs answer` the tool event really is
          the user acting — Claude Code's `AskUserQuestion` recovery depends on it, and an existing test
          caught the over-broad first attempt. A permission has no paired event reporting the approval.
        - **Rejected: watching the PTY for the user's Enter.** It cannot tell approve from deny (arrow +
          Enter picks *"No, and tell Codex what to do differently"*), is not specific to the prompt, and
          is the same class of guess step 7 already deleted once. The non-guessing version, if the stale
          badge ever needs removing, is to **correlate the resolving event by id** — `PermissionRequested`
          carries a `RequestId` and Codex payloads a `tool_call_id`; that would be proof rather than
          inference, and immune to the ordering race. Needs one measurement: whether the two agree on an
          id for the same call. Reasoning in full in *Codex hook findings*.
      - ✅ **Both follow-ups this opened are done — 2026-07-31, card #1094.** They turned out to be one
        change: *make Codex's transcript role identical to Claude Code's — enrichment only.*
        - **`CodexRolloutFinder` is deleted**, with `ITranscriptFinder`, `ITranscriptDirectory`,
          `TranscriptDirectory`, their registrations and `TranscriptPump.SearchAsync` — the whole
          find-by-convention path, ~200 lines, gone. Payloads name `transcript_path`, so both agents
          learn where their file is the same way. **Cost accepted deliberately:** a user who answers
          the hook-review screen with *"Continue without trusting"* gets no payloads, so nothing names
          the rollout and that card reports only what its process can say.
        - **The counts moved to the hooks**, so `CodexTranscriptNormalizer` no longer emits activity or
          turn ends and no longer sets `TurnCount`/`ToolCalls` — the divergence flagged above is
          undone, and `ITranscriptNormalizer`'s note with it. **One event stays**: `TurnFailed`, since a
          turn failing while the process stays alive and exits zero is invisible to `ProcessExited` and
          has never been *observed* on a hook payload. Measured, not assumed — drop it only after
          watching a real failed turn's `Stop`.
        - **And a bug the refactor would have re-introduced.** `PersistSessionIdAsync` lived on the
          finder's path; with hooks as the binding source, `SessionEventSink.Bind` only touched the live
          session object, so `card.SessionId` would have gone null again — the exact defect fixed
          earlier in this step. It now lives in `SessionEventSink`, guarded by an already-bound check
          because every payload carries the id and several arrive a second.
        - **Verified live:** session `019fbb34-…` bound from a hook payload and persisted to the card,
          `13k/258k` context from the rollout the payload located, 1 turn counted by hooks, and a
          timeline reading Launched → Executing → Turn ended → Your turn with **no** start-up-prompt
          detour, because `SessionStart` now arrives inside the grace window. 441 tests green.
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
  (`UserPromptSubmit`) as the one way out of Your turn — whatever the user's reason for
  typing, recovery is the same event, since they act in the terminal and ACT only ever
  learns about it.
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
- [x] **10. Session view + card actions — both adapters** — the full session view
  (terminal + right rail: identity, cwd, agent/model, context %, tokens, turns;
  back-to-board). Actions: Open terminal, **Open in Desktop**
  (`claude://resume?session=`), Kill, Retry, Complete. Explicitly **no
  approve/deny anywhere**. *Verify:* a task goes full-circle by hand on both agents,
  answering every prompt in the embedded terminal.
  ✅ **Closed 2026-08-01.** The board is now manually drivable end to end on both agents — the
  milestone's "plausible v1".
  - ✅ **Full circle, both agents, observed today.** Claude Code **#1100**: created, dragged to
    Ready, launched, walked to `to review` on its own reporting `claude-sonnet-5 · 46k/1000k ctx ·
    1 turns · 11k tokens`, then dragged to Completed → badge `done`. Codex **#1101**: the same
    walk, `14k/258k ctx · 1 turns · 4k tokens` → Completed → `done`. No server errors either run.
  - ⚠️ **One half of the verify was not re-done today: answering a prompt by typing in the embedded
    terminal.** It needs a *visible* window — the preview pane reports `document.hidden`, so xterm
    neither paints nor receives input, and no Chrome was connected to drive instead. It is not
    unevidenced: earlier live runs recorded in step 8 cover it on both agents — a Claude write gate
    answered in ACT's terminal (2026-07-30), and on Codex an `apply_patch` approval (#1092) plus
    `/plan` typed in by hand (#1097). What has not happened is one uninterrupted pass through the
    whole circle *including* the keystroke, in one sitting.
  - **A pre-session finding from the same run, worth keeping:** a Claude card pointed at a directory
    Claude Code has never seen (**#1099**) parks on its directory-trust prompt and ACT reports it as
    `needs permission` through the startup grace — the path the spec describes, seen here by
    accident rather than by design, and the reason the first attempt at this circle could not finish
    without a keyboard.
  - [x] **The blocked-card read-only statement is cut, and `lastMessage` with it —
    2026-08-01.** The badge is the whole report: a strip has no room for the sentence,
    the terminal that renders the prompt is one click away, and a copy on the card
    would go stale, since nothing reports a permission being approved. The field was
    the one thing feeding it and had **two writers and no readers** — the rules engine
    writing the block summary, transcript enrichment writing the agent's last message,
    each overwriting the other. Deleted: `Card.LastMessage`, `EnrichmentSnapshot.LastMessage`,
    both normalizers' extraction, and `BoardMove.Message`, which had no other consumer.
    - **One thing was worth saving out of it: the api-error text.** `TurnFailed`'s reason
      went through `Message` into that invisible field, so an `error` card explained itself
      nowhere. It now rides `Detail` into the transition `Note`, and `Transition_TurnFailed`
      gained a `{0}` in both languages — so the timeline reads *"The agent reported a failed
      turn: the model is not supported on this account"*.
    - **A design guard had to be re-cut rather than deleted.** `No_move_ever_carries_display_text`
      asserted `Detail` contains no space, which was the exit code's shape standing in for the
      real rule. The rule is about *provenance* — ACT's own wording is a `TransitionReason`
      resolved at display time, while a detail is verbatim data from the agent or the OS, and
      `Transition.Note` always named "a CLI error message" as exactly that. The test now pins
      each detail to the value its event carried.
  - [x] **Send back is cut too, and ACT now types nothing at all — 2026-08-01.** It only ever
    meant "reply to finished work without opening the terminal", and the terminal is a tab on
    the card: the compose box would have sat in the rail, inches from the real one. It was also
    the last caller-to-be of the paste-and-submit path, which is ACT guessing when a TUI is
    ready to be typed into — the guess step 7 paid for once and deleted from the launch path.
    Deleted: `IAgentTerminal.SubmitAsync`, `PtyAgentTerminal`'s paint/settle waits,
    `TerminalSubmitProfile` and its plumbing through `PtyAgentSession` and both adapters, and
    `AgentInputKind.Submit` / `AgentScript.AwaitsSubmit` in the test support. The contract
    suite's scripts pause on `AwaitsKeystroke` instead, which is what actually happens, and
    `ACT_types_only_what_it_submits_itself` became
    `Nothing_but_the_users_keystrokes_reaches_the_session` — the stronger assertion, and the
    one the spec's *input channel* rule now makes absolute.
    - **Left in place, and worth a decision at step 11:** `AgentResumeRequest.Message` — a
      resume's opening prompt, which rides the **command line** rather than the terminal, so
      it is not the same mechanism. Nothing in production sets it any more (both call sites
      pass null); reopen is the only feature that could want it, and reopen is undesigned.
  - [x] **Column gating on the task form — the debt step 5 deferred here. Landed 2026-08-01.**
    Every field used to be editable in every column, which broke the immutable-`initialPrompt`
    rule the moment a card launched. `Act.Core/Rules/TaskEditing.CanEditLaunchInputs` is the
    boundary — Preparing and Ready open, everything past the launch frozen — read off the
    **column**, like every other rule in `Rules/`, so a card whose pty died is still a card
    that ran.
    - **The split is what the run *was* versus what the next one *will be*.** Frozen: prompt,
      working dir, agent, schedule and the git action — the first three define the work that was
      asked for, and the last two are launch-time decisions on a card that has already launched,
      so editing them would change nothing while implying otherwise. Open: the **title**, which
      names the card rather than the work, and all of `launchConfig` — a model or permission mode
      does not touch the running turn, but it is what the next launch uses, which is exactly the
      "switch model, then Retry" path the spec asks for.
    - **Enforced in `NewTaskForm.ApplyTo`, not only in the markup.** A disabled input is a
      courtesy; the rule is that a launched card's prompt cannot change, and only the code that
      writes the card can promise it. The frozen assignments now sit behind the gate, so a save
      on a launched card writes the title and the launch config and nothing else.
    - **Presentation:** prompt and working dir are `ReadOnly` rather than disabled — a launched
      card's prompt is the thing you most come back to read, and a disabled textarea is neither
      legible nor selectable — Browse disappears, the three frozen dropdowns are disabled, and one
      `RadzenAlert` at the top of the form says why, once, instead of a note beside each control.
    - **Verified live** (both faces of the boundary): on launched #1038 the alert shows, prompt and
      dir are readonly, agent / schedule / git are disabled, model / effort / permission mode stay
      live, Browse is gone, and a save leaves title, prompt, dir and agent intact; on Preparing
      #1051 nothing is locked at all. `TaskEditingTests` covers every column and `NewTaskFormTests`
      the save path.
  - [x] **And the *Advanced* section is gone entirely — 2026-08-01, five fields, three fates.**
    The question that started it was whether those fields should be gated too, and the better
    answer was that most of them did not belong on a task at all.
    - **Deleted: `allowedTools` / `disallowedTools`.** Claude Code mapped them; **Codex ignored
      them outright** — no flag, no rejection, no adjustment — so a user could type a deny list on
      a Codex card, save it, launch, and never learn it was discarded. That is precisely the third
      outcome `LaunchConfigResolution` was built to make impossible. Making the drop honest was the
      alternative; deleting won because `permissionMode` is the guardrail that works on both, and
      `extraFlags` still reaches Claude's own flags for anyone who wants them.
    - **Moved to *Settings → Agents*: the executable, the extra flags and the environment.** They
      describe the **install**, not the work — where `codex.exe` lives does not change because the
      task does. New `AgentDefaults` (per agent, a list on `UserSettings` so adding an adapter does
      not touch the settings model), a collapsed fieldset per registered adapter, and
      `Act.Core/Agents/LaunchComposition` joining task-owned and machine-owned config on the way to
      the adapter — the single place they meet, used by launch, resume and preview alike.
    - **The composition clears before it applies**, so a card's own stale copy can never reach a
      CLI. That is what makes the move safe rather than a second source of truth.
    - **A one-time migration was mandatory, not a nicety.** A real board had the Store-packaged
      Codex path on its cards, and that install is on no `PATH` — dropping it would have failed
      every existing Codex card at spawn, for a value the user had already supplied and could no
      longer see. `AgentDefaultsMigration` lifts it at startup, newest card wins, **filling gaps
      only** so a value already in settings is never overwritten. Verified on a board that had the
      path: it logged the lift and the path appeared in the new settings section.
      - ⚠️ **Its first version inferred "already done" from whether a settings row existed, and that
        was wrong.** Any row written by an earlier build — or by the user touching one field — made
        the lift look complete, so the value it existed to rescue stayed lost with no way back but
        retyping it. Found on a real board where exactly that had happened. Now an explicit
        `UserSettings.AgentDefaultsLifted` marker says whether it ran, and the lift fills gaps
        instead of skipping populated agents, so a half-populated document repairs itself on the
        next start. `AgentDefaultsTests` pins the gap rules, including that whitespace counts as a
        gap and that a user's own value always wins.
      - ⚠️ **And a caution for verifying any of this from a sandboxed agent session.** Claude Code's
        desktop app runs under an MSIX container that redirects `%LOCALAPPDATA%`, so an app instance
        launched from inside a session writes to
        `…\Packages\Claude_…\LocalCache\Local\ACT\act.db` — a private copy — while the developer's
        own run uses the real one. Both answer to the same path, and a probe file written from
        inside appears at *both* locations, which makes the redirection look like proof of a shared
        file. It is not: they diverge, and "verified live" from a session says nothing about the
        real board. Check a card number that only exists on one side before believing either.
    - ⚠️ **It also lifted something to look at:** the newest Claude Code card carried an `env` block
      neutralising nested-session detection (`CLAUDECODE=0`, `CLAUDE_CODE_ENTRYPOINT=cli`, and four
      empty `CLAUDE_*` session variables), so that is now the **global** Claude Code default. It was
      one card's workaround for ACT being launched from inside a Claude Code session; whether it
      belongs on every launch — or belongs in ACT's own spawn code rather than in a user setting —
      is undecided.
  - [x] **Three settings features that came out of pruning the form — 2026-08-01.**
    - **Startup install discovery** (`AgentInstallDiscovery`, core port `IExecutableProbe`,
      `IAgentAdapter.Locate`). On `PATH` → change nothing; off `PATH` but installed → record the
      path; not installed → leave it empty and, first pass only, disable the agent. A path the user
      typed always wins. Candidates measured on a real machine: Claude Code at `~/.local/bin` or npm
      global; Codex via `CODEX_CLI_PATH` in `~/.codex/config.toml`, then the Store's build-hash
      folder newest-first. **Verified live:** *"ClaudeCode: found on PATH"* and
      *"Codex: found at …\OpenAI\Codex\bin\…\codex.exe"*.
    - **An `enabled` switch per agent.** The task form offers only enabled agents, plus always the
      one the card already carries — same rule a retired model follows. It also takes that agent off
      the **usage indicator** and stops the pump polling for it: a quota you cannot spend is not a
      number to act on. The pump tests the switch each tick rather than at startup, so it stops and
      resumes with no restart, and `UsageIndicator` subscribes to `UserSettingsService.Changed` so
      the meter leaves the bar on the click rather than at the next poll. Verified live: switching
      Codex off left the new-task agent dropdown offering `claude` alone and emptied the usage bar
      immediately; switching it back on restored the meter.
    - **The executable box validates and browses.** `AgentBinaryCheck` answers by the launch's own
      resolution rules — no separator means a `PATH` name, anything else a path that must exist — so
      the box cannot look fine while the launch fails with "not found". A warning rather than a
      block.
      - ⚠️ **The empty case shipped wrong and was fixed the same day.** It asked the adapter whether
        the CLI was found *anywhere* and rendered that as "Found on PATH" — so an emptied Codex box
        was reassuring about a launch that resolves the bare name and fails, for a binary sitting on
        the disk. Empty means "resolve the name", so the only honest answers are on-`PATH`,
        installed-**off**-`PATH` (a warning naming the path, plus a **Use it** button that fills the
        box) and not-installed. `AgentBinaryCheck` returns an `AgentBinaryState` carrying the
        discovered path, and `AgentBinaryCheckTests` pins all three so the distinction cannot
        collapse again. `FolderPicker`
      became **`PathPicker`** with a file mode to serve the Browse button: same walk, files listed
      after folders, and clicking a file *is* the pick, so the confirm button is hidden.
      `IWorkingDirectories.List` gained `includeFiles`, off by default — enumerating files on every
      rung of a directory walk is a cost the working-directory picker should not pay. Verified live:
      a bogus path warns, Browse opens at the nearest existing folder, clicking a file fills the box
      and closes the picker, and the task form's directory picker still lists no files and keeps
      *Use this folder*.
    - **A new-task template** (`TaskDefaults`): working dir, agent, model, effort, permission mode,
      schedule, git-when-done — everything but title and prompt. Changing the default agent clears a
      model or effort it does not offer and moves the permission mode into range, so the template can
      never hold a value that would be rejected at launch. Verified live: a default working directory
      appeared on `/card/new` with title and prompt still empty.
      - ⚠️ **Superseded 2026-08-04 — it is a managed list now, on its own pages.** `TaskDefaults`
        became `TaskTemplate`, one block became `UserSettings.Templates`, and the settings section
        became a pointer to `/templates`. A template also carries the **prompt and the title**, which
        the defaults deliberately did not: the argument against pre-filling either is about a
        *default*, and they are most of the value of a template. Both are optional, and a template
        with a title needs no CLI call to name the cards it makes. The list is sorted by name, the name
        is required, and the shipped default is named *Default* and is the default for good — there is
        no promote action. Dressing the picker also turned up two app-wide styling bugs that had been
        there since the shell was built: everything ACT styles itself was set in **Times New Roman**,
        and every outlined button was ringed in near-white off Radzen's raw ramp. Both are in
        `design-notes.md` → *Styling against Radzen*. The store change is the migration
        list's first real entry
        (schema 1 → 2). See the spec's *Task templates* and `design-notes.md` → *The store*.
        Verified live against a hand-written schema-1 store: the old defaults came back as the
        default template with the rest of the settings document untouched.
  - [x] **Retry — built 2026-08-01, and three of the spec's assumptions were dropped on the way.**
    `Act.Core/Rules/CardRetry` is the predicate; `SessionLauncher.RetryAsync` the action, sharing the
    launch path so there is one place a session is started. The button lives on the **flight strip**,
    not the session rail: opening a failed card's terminal already resumes it, so by the time you are
    at the rail the process is live again and typing into it is your job.
    - **It does not re-send `initialPrompt`.** A session forty turns deep would be told to start the
      work over — by then the opening instruction describes a beginning that no longer exists, and
      the transcript the resume just reopened is the real context. `Act.Core/Agents/RetryInstruction`
      says only that the session was interrupted and to continue, localised like `AutoGitInstruction`
      because ACT addresses the agent in the user's language. It rides the resume command line, so
      ACT still types nothing.
    - **It is offered only when the process is gone.** A live CLI is one the user can type into, and
      the terminal is a click away; retrying it would mean killing a usable session. This makes Retry
      the case the user genuinely cannot do themselves, and it needs no kill, no interrupt and no new
      input path.
    - **No fresh-seed fallback.** The spec promised one when the transcript is gone; **Duplicate**
      already makes a new card with the same config, so the fallback would be a second way to do an
      existing thing, with its own dialog and its own "which path was taken" bookkeeping.
    - ✅ **Verified live at the predicate:** the Retry button appears on exactly one card — `#1038`,
      `error` with a dead session — and on no other card in any column or badge, including the other
      `error` card whose session is live.
    - ⚠️ **Not yet verified: that the continue-message reaches the agent.** This is the assertion step
      7 learned to make — the prompt-truncation bug survived two steps because every check asked
      "did events arrive, did the card move", both true of a session that only greets you — so the
      real proof is finding the message in the transcript, not watching the badge. Pinned at the unit
      level (`Resuming_passes_the_session_id_and_the_message_positionally`) but not end to end.
    - ✅ **An invalid model is a *pre-session exit*, not a failed turn — measured 2026-08-01, card
      #1098.** Launched with `--model gpt-not-a-real-model` through the per-agent extra flags, Codex
      **exits with code 2 before any session exists**: the timeline reads Launched → Executing →
      *"Agent exited with code 2"* → Your turn, eight seconds end to end. So the CLI validates the
      model itself rather than starting a session and letting the api reject it. Two things this
      confirms: the `ProcessExited` → `error` path works end to end, and **`CardRetry` correctly
      refuses the card** — no session was ever bound, so there is nothing to resume and Duplicate is
      the right answer. The Retry button was absent on #1098 and present on #1038, which is the
      predicate discriminating on real data.
    - ⚠️ **So `TurnFailed` still has not been reproduced, and now it is clearer why.** It needs a model
      the **CLI accepts but the account cannot use** — the original measurement's `gpt-5.4` — so that
      a session starts and the *request* is rejected. Client-side-invalid names exit early instead.
      Also measured and worth recording: **`OPENAI_BASE_URL` is ignored under ChatGPT auth**, so
      pointing it at an unreachable host does not fail a turn; the run went to the real endpoint and
      succeeded. Whether a Codex TUI still accepts input after `task_complete` carries an `error`
      remains unmeasured — **which does not block the feature**: refusing Retry to a live session is
      the conservative answer under either outcome, and the measurement could only show the restraint
      to be unnecessary.
  - [x] **The rail's context % and tokens — 2026-08-01, the last of the step's own list.** The
    session rail knew *less* about a run than the board strip did, which is backwards: the terminal
    is the surface you sit on while it works. It now carries the percentage and the same 3px context
    bar the strip uses, cumulative tokens, and compactions when there have been any — all from
    `CardMetrics`, which already had `ContextPercent` and `TokensTotal`, so nothing new is ingested
    or computed. A model whose window ACT does not know still shows no percentage rather than a
    wrong one. `Text.Thousands` moved out of `FlightStrip` so both surfaces round the same number
    the same way; a card disagreeing with its own terminal would read as a bug.
    - **Launch moved in with the other actions.** It sat alone at the top of the rail while Complete,
      Open in Desktop and Kill sat at the bottom — one place to act and one place to read is the
      rule, so the button joined the group (first, and the only Primary when it shows) and the
      *"no session is running"* line stayed with the facts, where a status belongs.
    - **Verified live:** #1042 reads `142k / 200k 71%`, 18 turns, 356k tokens, no compactions row;
      #1040 reads `88k / 200k 44%`, 7 turns, 223k tokens, 2 compactions, with the bar measured at
      105.6 of 240px — 44%, matching its own number.

## Phase 4 — Completion

- [x] **11. Complete + reopen** — Your turn → Completed and reopen. *Verify:* a
  signed-off task lands in Completed; reopen resumes. ✅ **Closed 2026-08-01** — both manual
  transitions ship, and the third thing this step used to carry (the completion/git modal) was
  cut rather than built.
  - **Auto-complete was cut on 2026-07-30** — see the spec's *No auto-completion*.
    Completion is always the user's drag, so this step is now just the two manual
    transitions, and nothing in ACT needs the agent to assert that it finished.
    **`autoGit` survived** and left this step entirely: it is a prompt suffix
    (`AutoGitInstruction`), applied at launch and unrelated to completion.
  - [x] **Your turn → Completed, brought forward.** `Act.Core/Rules/CardCompletion` is the
    predicate (its own rule, because reaching Completed stamps the sign-off and ends the session,
    which `BoardState.MoveAsync` neither does nor should — and because no event may decide it),
    and `Act.App/Sessions/CardCompleter` is the action, the mirror of `SessionLauncher`. It stamps
    and persists the card *before* tearing the terminal down, so anything the dying pty still
    reports lands on a card the engine no longer governs. The gate is the **column**, so
    every card in Your turn can be signed off, `error` included.
    Verified live: a launched card walked to Your turn and was signed off from both surfaces —
    Completed, badge `done`, `Marked completed` on the timeline, agent process gone.
  - [x] **The sign-off is a drag, not a button.** A Your turn card now lifts (`ManualMove.CanDrag`)
    and Completed is its only legal drop (`CardCompletion.CanCompleteInto`), which `BoardView`
    routes to `CardCompleter` rather than to `MoveAsync`. The strip's Complete button is gone —
    the board has one gesture for moving a card — and the session view keeps its rail action for
    a card you are already inside.
  - [x] **Reopen — Completed → Your turn, the mirror of the sign-off — 2026-08-01.**
    `Act.Core/Rules/CardReopen` is the predicate and `Act.App/Sessions/CardReopener` the action,
    paired with `CardCompletion`/`CardCompleter` the same way. Completed now lifts
    (`ManualMove.CanDrag`) with Your turn as its only legal drop, and the session view carries a
    **Reopen** rail action where Complete sits for a card in the other column.
    - **It is its own rule rather than a manual move for a concrete reason: it un-stamps
      `completedAt`.** That field is the retention clock (`CompletedRetention` dates a card from
      its sign-off), so a reopen that left the stamp behind would leave a card in Your turn that
      the sweep could still archive out from under the user. `BoardState.MoveAsync` neither does
      that nor should, which is the same argument that gave completion its own rule.
    - **It lands on `to review`,** because nothing was observed — the card is simply back in the
      user's court with work to look at — and **starts no session**: a reopened card gets its
      terminal back the way every bound card does, by being opened (`SessionRestore` already
      covers Completed, and Your turn is restored unattended at the next start). So "reopen
      resumes" in the verify is the *terminal* resuming on open, which shipped at step 10.
    - **An auto-archived card does not reopen.** `CanReopen` gates on `IsOnBoard`, not just
      `!IsDeleted`: retention archives Completed cards, so this is the one case an unqualified
      column check would let through — the card would land in Your turn and off the board at the
      same time. A card off the board comes back through the archive's restore.
    - **Verified live 2026-08-01, card #1052.** Picking it up lit Your turn and grayed every
      other column; the drop moved it there on `to review`; the rail's Reopen did the same from
      inside the card and left the page where it was, its action swapping to *Mark completed*.
      The timeline reads `Marked completed → Completed`, `Reopened → Your turn` for both paths.
      563 tests green.
  - [x] **The completion/git modal is cut, not built — decided 2026-08-01.** With `autoGit`
    already re-cut as a prompt suffix, the modal would have taxed *every* sign-off to ask a
    question the suffix answers at creation, and the spawned ACT-emitted git card would have been
    a second session for deterministic work. Git chosen at review time needs no mechanism: the
    terminal the user just reviewed in is open in front of them. Reasoning and the doc rewrite are
    in the spec's *Git integration*; the follow-on edits removed the last ACT-emitted spawn, so
    every spawn is now agent-emitted. **No code changed** — completing was already a plain action.

## Phase 5 — Automation (on a proven base)

- [x] **13. Native notifications** — OS pings for attention states; actionable,
  focus-aware, coalesced. *Verify:* backgrounded ACT pings on needs-you.
  ✅ **Landed 2026-08-01**, with one word of the line above deliberately unbuilt: see *no digest*.
  - [x] **The decision is a pure rule; everything else is policy in the app.**
    `Act.Core/Rules/NotificationTrigger.Wants(card)` is the whole of it — on the board, in Your
    turn, wanting attention — and it reads `Card.NeedsAttention` rather than listing the five
    badges again, because the pulse on the board and the ping on the desktop are the same claim
    and a card that blinks without pinging is a bug nobody notices until the night it matters.
    Reading the *card* rather than the event is what lets both writers of an attention state ask
    the same question: `SessionEventPump` after a move, and `SessionLauncher` after a failed
    launch. It also makes asking twice harmless, which the dedupe below relies on.
  - [x] **`Act.App/Notifications/` holds the policy.** `NotificationDispatcher` owns the setting,
    the focus gate, the one-ping-per-state rule and the wording; `UiPresence` is how a server-side
    decision knows whether anyone is looking (`MainLayout` reports focus + route per circuit, from
    `act-presence.js` watching `focus`/`blur`/`visibilitychange` — both halves, because a minimised
    window can still report focus); `DeepLinkRouter` turns a clicked toast into a navigation on the
    live circuit rather than a page reload.
  - [x] **`INotifier` is an `Act.App/Desktop` port, not a Core one.** Nothing in Core calls it once
    the decision is a pure rule, which is the same test that keeps `IDesktopBridge` out of
    `Act.Core/Abstractions`. `ElectronNotifier` + a no-op `BrowserNotifier`, registered and
    `Replace`d exactly as the bridge is. The doc line that placed it in Core is corrected.
  - [x] **No coalescing — decided 2026-08-01, against the step's own one-liner.** A digest saying
    *"4 need you"* cannot deep-link to any of the four, and the deep link is the thing that makes
    the notification worth having. So bursts stay one toast each, and the whole debounce/digest
    machinery is not built. What carries the anti-spam job alone is **one state, one ping**:
    `(taskId, badge)` is remembered and cleared when the card leaves that state, so a re-block
    after the user answers pings again and a card written twice for the same state does not.
  - [x] **One switch, not a per-kind matrix** (*Settings → System*, on by default, desktop only).
    Nobody wants to hear that a task failed but not that one finished, and every extra toggle is
    another way to silence the signal unattended mode depends on.
  - [x] **A suppressed state is not replayed.** The dedupe entry is written *before* the setting
    and focus gates are consulted, so a card the user watched change on the board does not fire a
    toast when they later walk away. The notification is about the moment it changed.
  - [x] **`ElectronBoundaryTests` replaces the project split that was considered and rejected.**
    Making `Act.Desktop` a real csproj only isolates anything if `Act.App` becomes a library and
    the desktop host becomes the exe — which moves every static asset to `_content/Act.App/…` for
    ~350 lines of shell code. Instead a test fails the build if anything outside `Program.cs`,
    `ServiceCollectionExtensions.cs` and `Desktop/` names ElectronNET, and the empty
    `src/Act.Desktop/` placeholder is deleted. Full reasoning in *repository-structure*.
  - ✅ **Verified live in the Electron shell, window minimised** — cards #1102 and #1103, driven
    from a second browser client on the same host so the desktop window stayed backgrounded.
    #1102 parked on Claude Code's directory-trust prompt and raised **`Needs permission / #1102 ·
    Notification check`**; #1103 finished a turn and raised **`Ready for review / #1103 · Toast
    identity check`**. 614 tests green.
    - ⚠️ **Two identity bugs the live run exposed, one fixed and one accepted.** The first toast
      announced itself as **`electron.app.Electron`** and carried no icon: Windows takes both from
      the Application User Model ID, which Electron leaves at its own default. Fixed by setting
      `com.northlabkev.act` (the GitHub org, hyphens dropped as reverse-DNS wants) at startup and
      putting the *same string* in the installer's `appId` — the
      NSIS shortcut is what maps the id to a product name, so a mismatch would leave the packaged
      build no better off — plus `NotificationOptions.Icon` pointing at the same `icon.ico` the
      window and tray use, which works in both modes. **Unpackaged runs still show the raw id**,
      because `dotnet run` has no Start-menu shortcut to resolve a name from; accepted rather than
      fixed, since the alternative is ACT writing a display-name key into the user's registry on
      every machine it runs on.
    - **What is not verified by hand: the click-through.** The toast's `OnClick` reveals the window
      and routes to the card's terminal, and nothing in the harness can click a Windows toast. The
      pieces either side of it are covered (`DeepLinkRouter` → `MainLayout` navigation, and
      `DesktopShell.RevealAsync`, which the tray already uses), but the join is unexercised.
    - **Also learned, and reusable:** a screen capture taken from a *background* PowerShell task is
      blank — it runs on a non-interactive window station. Anything that has to see the desktop has
      to run in the foreground.
- [x] **14. Scheduling & queue runner** — `schedule`, `maxConcurrent`,
  `dependsOn` ordering, usage backpressure, keep-awake, and the global
  **auto-execution pause switch**. *Verify:* a queue of Ready
  tasks processes unattended across a window reset; pause halts all auto-launch.
  ✅ **Landed 2026-08-02**, 696 tests green. Verified live on the real board: card **#1105**
  was created, dragged to Ready on `now`, and **launched by the runner on its own** the
  moment the cap was raised past the occupied count — no button pressed — then walked to
  Your turn on Claude Code's directory-trust prompt and was signed off. Beside it **#1104**
  sat in Ready under `folder #1099` and never started, and both showed `paused` the instant
  the top-bar switch was clicked. What was **not** exercised is the one thing that needs a
  five-hour boundary to happen: an armed `next window` card firing at a real reset. That is
  an observation to make, not a thing to build — the arming path is unit-tested and the
  `now` path is the same code from `IsDue` onward.
  - **Decisions taken 2026-08-01, before any code.** Each one changes where the code
    lives or what the board says, so they are settled here rather than discovered:
    - **`waiting-reset` is renamed *usage limit reached*.** The old name described the
      remedy rather than the condition, and the condition is what the card has to say.
    - **Every hold has its own chip, not one `queued`.** There are six distinct reasons a
      Ready card is not running and they clear at wildly different times — a cap frees in
      minutes, a weekly quota in days — so a single word would flatten the one thing the
      user needs. See *The chips* below.
    - **A hold is a derived chip, never a `Badge`.** `Badge` is persisted and drives
      the blink and the toast; a hold is transient, computed,
      and must raise no attention at all. It renders **in the badge slot**, which a Ready
      card leaves empty, in the muted register the `quiet` chip established.
    - **`dependsOn` gets its gate and no UI.** Its only producer is step 12's
      `create_followup`, which is the last step ACT builds; the gate is cheap and
      unit-testable now, and a dependency picker nobody can populate is not.
    - **The runner reads the same `UsageState` the top bar reads.** No second poll, no
      extra request to either vendor — backpressure is a consumer of the existing pump.
    - **A usage reading ACT cannot get does not stop the queue.** An unavailable probe
      launches anyway: a broken credential file must not silently freeze an overnight
      run. (A card whose *schedule* is a window boundary is the exception — it stays
      unarmed, because "later" cannot be resolved without a reset instant.)
    - **FIFO**, ordered by the eligibility instant and then by card number.
    - ~~**`UsageWindow` gains its declared length**, so *window after next* is
      `resetsAt + length` rather than a hardcoded five hours.~~ **Both are gone —
      2026-08-02**, see step 15's item 6: the schedule was removed, and `Length` /
      `Duration` had no other reader.
    - **A headless launch uses the last terminal geometry**, not a constant — an
      unattended card that is opened later should not have to reflow from 120×30.
  - **The chips**, one per reason, shown in the badge slot with the full sentence in the
    tooltip. The dashed schedule chip below stays what it always was — the *intent* —
    so a Ready strip reads intent on one line and reality on the other:
    `queued 5/5` (cap full) · `folder #1041` · `after #1043` (dependency) ·
    `usage 2h14m` (limit reached, counting to reset) · `paused` · `agent off`.
    Precedence is most-durable first — paused, agent off, usage, folder, dependency,
    slot — because only one fits. No chip at all when the card is `manual`, or armed and
    simply not due yet: the intent chip already says when.
  - [x] **1. Settings + model.** ✅ `MaxConcurrent` (default **5**, clamped 1–20) and
    `AutoExecutionPaused` on `UserSettings`, both in *Settings → Execution*; `Card.EligibleAt`
    for the armed instant; `UsageWindow.Length` with a `Duration` that falls back to the
    nominal length for the kind — Codex declares `limit_window_seconds`, Claude Code declares
    a `kind` and nothing else, and *window after next* needs a number either way. **Both fields
    were deleted at step 15 with the schedule that was their only reader.**
  - [x] **2. `Act.Core/Scheduling/` — eligibility as pure logic.** ✅ `LaunchQueue.Evaluate`
    returns the ordered launch list, the per-card hold **and** the instants to arm, out of one
    pass, so the chip on the board cannot disagree with what the runner did. Parts:
    `ScheduleArming`, `ConcurrencySlots`, `DependencyGate`, `UsageBackpressure`, `SleepPolicy`,
    and `WorkingDirConflict` reused.
    - **The pass had to be able to judge its own decisions.** Two Ready cards in one folder are
      both eligible and neither is Executing, so `WorkingDirConflict.Blocking` says yes to both
      and the pass would launch two agents into one working tree — the exact collision the guard
      exists to prevent, reintroduced by the thing that was supposed to honour it. The rule gained
      `SameFolder`, the folder comparison without the column test, which the pass asks of the
      cards it has already claimed this round. Unit-tested as
      `Two_ready_cards_in_one_folder_do_not_both_launch`.
  - [x] **3. The runner pump.** ✅ `Act.App/Sessions/QueueRunner`: a 20-second backstop tick plus
    `BoardState.Changed` / `UsageState.Changed` / `settings.Changed`, single-flight like
    `RetentionPump`, launching sequentially. Started **after** `SessionRestorer` — a restored card
    occupies a slot and holds its folder, so a runner that went first would judge an empty board
    and launch straight past the cap.
    - **The changes it causes are coalesced, not queued.** Every launch and every arming is a card
      write that raises `Changed`; without a pending flag, one pass over five cards schedules five
      more that each find nothing to do. The flag is cleared *inside* the gate, so a change landing
      during a pass still schedules the next one — it suppresses duplicates, never the news.
  - [x] **4. The hold chips** on the Ready strips, derived, both densities. ✅ Plus the thing the
    chips exposed: **the board had no way to hear about them.** They are computed from the pause
    switch, the cap and the usage reading — none of which is a card write — so a toggled pause left
    every strip saying the opposite of what the queue was doing, which is exactly the disagreement
    the single-evaluation design exists to prevent. `QueueRunner.Evaluated` fires after each pass
    and `BoardView` re-renders on it.
  - [x] **5. The unattended warning** in the task form. ✅ Verified live: `now` + `default` raises
    it, `now` + `don't ask` clears it, and it never blocks a save.
  - [x] **6. Keep-awake becomes task-aware.** ✅ Held only while there is auto-work to protect —
    a Ready card that can actually auto-launch, or live work — and released otherwise, **including
    while `AutoExecutionPaused` is on** unless something is already running. The ownership moved
    with it: `UserSettingsService` no longer takes `ISleepInhibitor` at all, because it knows the
    user wants a hold and not whether there is anything to hold for.
  - [x] **7. The working-directory guard queues rather than refuses.** ✅ Both, and the split is the
    point: a launch the user just pressed is still refused with the card named, because nothing
    needs undoing when the folder frees; the runner reads the same non-null answer as "not yet".
    No change to the rule beyond item 2's `SameFolder`.
  - [x] **8. Live verify + docs.** ✅ Spec (*Runner logic*, *Ready-card indicators*, *Keep-awake*,
    *Master switch*, the refused-not-queued bullet, `eligibleAt` in the data model, the
    `waiting-reset` name retired), `repository-structure.md`, and the ticks here.
  - ⚠️ **The cap and keep-awake draw *nearly* the same line, and merging them was a mistake.**
    Mid-step the cap was narrowed to exclude `error` and `killed` — the dev board showed eleven
    occupants against a cap of five, almost all of them sessions that died weeks ago, and "no
    process, no slot" looked obviously right. **Reverted on the same day**, because the evidence was
    a test board's wreckage and not something a real board accumulates: an `error` card is a failure
    the user has not dealt with, one Retry from being live again, and a queue that launched past a
    growing pile of them would turn one broken task into twenty. So the cap stays exactly as the
    spec wrote it — Executing plus Your turn on any badge but `to review`.
    - **What the detour did leave behind is the right split.** `SleepPolicy` had been reading
      `ConcurrencySlots.Occupies`, so reverting would have made a card that died at 3am keep the
      laptop up until morning. It now has its own predicate, one badge tighter: the cap counts
      *work in flight*, sleep protects *processes*, and `error` / `killed` are the two badges where
      those differ. Both rules say so in a comment, because the next reader will assume they are
      the same set.
  - ✅ **The top bar's `auto-exec on` chip was a hardcoded lie, and is now the switch.** It had been
    a mockup leftover since the shell was built; adding the setting would have made it worse — a
    chip claiming the queue was live while it was paused. It is now a button: it reflects the
    state, drops its green dot and turns amber when paused, and toggles on click. The master switch
    is what you reach for when something is going wrong, and Settings is two navigations away.
    - **Moved off the top bar at step 15**, into the board's control row beside the filter box, and
      the density toggle joined it there. It governs the board and nothing else, and the board is one
      back-arrow from every page — so it kept its reachability and gave the top bar back to the app.

## Phase 6 — Breadth & polish

- [x] **15. Persistence polish** — auto-archive (**landed**: 10-day default,
  `archivedAt` its own marker, `CompletedRetention` + `RetentionPump`, the knob in
  Settings → *Retention*), the transitions timeline (**landed**, see below), and
  **finding a card again** — the only open work. *Verify:* old Completed cards archive
  and stay searchable. ✅ **Landed 2026-08-02**, 717 tests green, and the verify line met on the
  real board: `shell` typed into the archive's box found **#1031 *Empty app shell***, an
  auto-archived Completed card, from a store of 31 archived rows.
  - [x] **The timeline was already built, and as a page rather than a drawer.**
    `TimelineView` is the third tab on a card (`/card/{id}/timeline`), rendering
    `Transitions` oldest-first with the badge tokens on the rail — a row's dot is the
    colour the badge was when it happened — and falling back to "entered &lt;column&gt;"
    for rows written before `TransitionReason` existed. This entry said *"in the drawer"*
    from before the drawer was replaced by pages (the spec's *Every surface is a page*);
    the wording was stale, not the work.
  - **Decisions taken 2026-08-02, before any code.** The spec says "fully searchable" and
    nothing more, so every one of these is settled here rather than discovered:
    - **Two filter boxes, not a search page.** The two places you go looking for a card
      are the board and the archive, and filtering *in place* leaves each result on the
      surface it belongs to, in the strip or the row the user already reads. A `/search`
      page was the alternative and was rejected: it is a third list of cards, needing its
      own row design, its own ordering rule and its own answer to "where is this card
      now" — all to show what the two existing surfaces already show correctly.
      - **The cost, and how it is paid.** No single query spans both surfaces. So the
        **board box reports what it cannot show**: while a query is active it carries a
        link reading *"N archived match"* onto the archive, filter and all. Cheap, because
        `BoardState.All` already holds every card in memory.
    - **The filter narrows what is *drawn*, never what is counted or what blinks.** A
      column header reads `3/12` while a query is active, and the attention treatment —
      rail glow, pulse, `!` corner — keeps answering for the whole column, matched or not.
      A board that could hide a card needing you is the one failure this app exists to
      prevent, and a filter is a view, not a truth.
    - **Match on number, title, working directory and the initial prompt.** The first
      three are how a card is identified, the fourth is where the recall value actually
      is ("the one where I asked it to fix the pty quoting"). Agent / model and transition
      notes were considered and left out: both match far too broadly to be typed into a
      box whose results are supposed to narrow.
    - **Semantics: whitespace-split terms, all of which must match (AND), case- and
      diacritic-insensitive.** French is a shipped UI language, so `IgnoreNonSpace` goes
      in beside `IgnoreCase`. A term that is digits — with or without a leading `#` —
      also matches the card **number by prefix**, so `#104`, `104` and `1042` all find
      `#1042`. No ranking and no fuzzy matching: the board's order is the column's and the
      archive's is newest-first, and filtering must not reorder either.
    - **Nothing is persisted and nothing is stored.** The query is view state, cleared by
      `Esc`, by the clear button and by navigation. No `ICardStore` change, no LiteDB
      index, no debounce — every card is already in memory and a substring scan over a few
      thousand of them is free.
  - [x] **1. `Act.Core/Rules/CardSearch` — the predicate, and the only unit-tested part.**
    ✅ Term tokenizer + `Matches` + `Filter`, pure, 21 tests: the AND across terms, the case
    and accent folding both ways, the numeric prefix rule (including that a numeric term
    **still** reaches the text fields, so `220` finds a title reading *"Bump to 2.1.220"*, and
    that `042` does **not** find `#1042` — a prefix, not a substring), an empty query matching
    everything, a `#`-prefixed non-number being ordinary text, and that filtering hands back
    the order it was given.
  - [x] **2. The shared filter control, and the board.** ✅ One `CardFilter` (Radzen text box,
    search icon, clear button, `Esc`) two-way bound, used by both pages. Column headers read
    `shown/total` while a query is live.
    - **The "attention keeps answering for the whole column" decision needed a control that
      did not exist.** The blink is rendered *per strip*, not per column — so a filtered-out
      needs-you card does not blink quietly, it disappears. The column header now carries a
      muted amber **`N hidden`** chip counting exactly those cards, which is the honest form of
      the same promise.
  - [x] **3. The archive filter and the cross-link.** ✅ Same control on `ArchiveView`, the
    board's *"N in the archive ›"* link carrying the query over as `/archive?q=…`, and the
    receiving page seeding its box from it **once** — a cascading value changing must not throw
    away what the user has typed since.
    - **And *Clear archive* is hidden while a filter is up.** The spec says it empties exactly
      what the list shows; a filtered list is not what it would empty, and the one irreversible
      action in the app must not be able to mean two things.
    - ⚠️ **The archive's box could not be typed into, and the cause was the window title bar.**
      Reported by the user, 2026-08-02: the field read as *read-only*. Every page header carries
      `class="bar"` and `MainLayout`'s `::deep .bar` reaches page content, so all five of them
      inherit the title strip's `-webkit-app-region: drag` and `user-select: none` — and an OS
      drag region swallows the mouse, so the input never took focus. The existing exceptions
      punched out `.rz-button` and `a`, which is why every *button* in that header always worked;
      the archive filter is simply the **first input ever placed in a page header**. Fixed by
      adding `input` / `textarea` to the same exception and giving them their selection back.
      - **Why the live verify missed it:** the preview pane delivers no keystrokes, so the query
        had to be dispatched as an `input` event — which bypasses focus entirely and would pass
        against a field the mouse can never reach. Reading the computed style (`user-select`
        inherited as `none` on both the header and the input) is what named the cause, and is
        the check worth making whenever a control lands in a `.bar`.
  - [x] **5. The board's control row — added 2026-08-02, on the user's call.** ✅ The filter box
    opened a strip of space between the top bar and the columns, so the board's own switches moved
    into it, right-aligned: the **auto-execution master switch** (down from the top bar) and a new
    **density chip**. Two clicks that used to cost a trip to Settings, on the surface they change.
    - **The density label is now *Detailed*, not *Spacious*.** The setting's hint always said *how
      much detail each flight strip shows*; "spacious" described the whitespace instead of the
      point. *Standard* was the other candidate and lost for saying nothing about what it does —
      and for implying the other mode is not standard. **The rename goes all the way down** —
      `BoardDensity.Detailed`, the resx key, the CSS class — so no code keeps a name the product
      does not use.
      - ⚠️ **And the stored value bites back — the plan was to rewrite the one database by hand, and
        that turned out to be both impossible from here and insufficient.** `BoardDensity` is
        persisted by name, so a document still saying `Spacious` is a name this build does not have.
        Measured rather than assumed: `ArgumentException: Requested value 'Spacious' was not found`,
        thrown out of `BsonMapper.Deserialize`. Settings load in a **constructor at start-up**, so
        unlike a retired `Badge` — which costs one unreadable card — this takes the whole app down.
        So `ActBsonMapper` now maps `BoardDensity` like `Badge` and `TransitionReason`: unknown name
        → the default, next save writes the current one. Guarded by
        `A_density_name_this_build_retired_loads_as_the_default`, which fails with that exact
        exception without the converter.
        - **Why the by-hand rewrite could not be done:** the agent's tools run inside the Claude
          desktop app's MSIX container, where writes to `%LOCALAPPDATA%` are **redirected** to
          `…\Packages\Claude_…\LocalCache\Local\`. Reads pass through to the real file until the
          first write, which is why the db read back correctly and the app launched with real data
          — the agent was verifying its own copy. Both paths hash identically from inside and
          diverge from what the user sees. **Anything under `%LOCALAPPDATA%` an agent claims to
          have written on this machine is suspect; have the app or the user do it.**
    - The chip **names the mode you are in**, not the one you would get: the board in front of you
      is the answer, and a button labelled with the other mode would contradict it on every glance.
    - Verified live: both chips render in the row, the top bar is down to archive + settings, the
      density chip flips `board detailed` ↔ `board compact` and its label with it, the switch goes
      amber on `statchip paused`, and both persist through the settings service.
  - [x] **6. `window after next` removed from the schedule choices — 2026-08-02, on the user's
    call.** It said "two resets from now", which is a duration wearing a boundary's clothes: what a
    user wants that far out is a time, and `datetime` already says one exactly. Gone from
    `TaskSchedule`, `ScheduleArming`, both choice lists (task form and task defaults), the strip's
    intent chip and four resx entries.
    - **It took `UsageWindow.Length` and `Duration` with it** — added at step 14 for this one
      arithmetic (`resetsAt + length`) and read by nothing else once it left. Codex still measures
      `limit_window_seconds`, but only to `Classify` the window, which is what it was always for.
    - **A stored `WindowAfterNext` reads back as `Manual`**, not null: the mapper is asked this of a
      non-nullable property too (the task defaults in settings), and a card whose schedule ACT can no
      longer resolve should wait for a hand rather than launch on a guess. Guarded by
      `A_schedule_this_build_retired_reads_back_as_manual`. **This is the third retired enum value in
      this codebase and the second in one day** — `Badge`, `TransitionReason`, `BoardDensity`, now
      `TaskSchedule`. Retiring a persisted name without a converter is a start-up crash, and the
      converter is four lines; add it in the same commit as the deletion, every time.
  - [x] **4. Live verify + docs.** ✅ Spec (*Finding a card*), `repository-structure.md`, the
    ticks here. Verified on the real board, all four claims: `rollout` narrowed every column to
    `0/n` but one and raised **`20 masquée(s)`** on Your turn; `#109` matched exactly
    #1090–#1099 by prefix; `shell` found the auto-archived **#1031** plus a card matching on its
    *prompt*, with the purge button gone; the handoff link read `2 dans les archives ›` →
    `/archive?q=shell` and landed pre-filtered; `Esc` cleared the box, restored all 31 rows and
    brought the purge button back. No console errors, no server errors.
    - **DOM-level, not visual — and the visual half is now closed.** The preview pane is not
      displayed, so it composites no frames: a screenshot times out, and the pane also swallowed
      the synthetic keystrokes (the queries had to be dispatched as `input` events). Every
      assertion above was read out of the DOM. ✅ **The user looked at both boxes in a real
      window on 2026-08-02** and they render correctly, so this is no longer a carry-over into
      step 16.
- [x] **16. UI polish** — final spacing, type, Radzen theming pass. **The settings page
  below already shipped**, and two of its listed knobs were decided away rather than built
  (notification matrix → one switch; weekly-reset time → served over HTTP, so there is
  nothing to configure).
  ✅ **Closed 2026-08-02**, all three sweeps landed: the app is now driven by scales for space, type
  and colour rather than per-component values, and the widget library is inside the same palette
  instead of beside it. **One thing was not done, and is closed rather than pending:**
  - **Nothing was looked at in a real window.** Every claim below is read out of the DOM: the
    preview pane composites no frames, so a screenshot times out. All three sweeps changed numbers
    that were *measured* before and after — including contrast ratios — which is why this was an
    acceptable trade, but the aesthetic judgement a polish step normally ends with has not been
    made by anyone but the user, at their own screen.
  - [x] **Spacing — swept 2026-08-02.** A step scale (`--act-s1`…`--act-s6` = 4/8/12/16/24/32px)
    in `app.css` beside the colour tokens, plus `--act-bar-pad`, `--act-page-pad`,
    `--act-sheet-max` and `--act-control-w`; every page-level and container-level edge across
    the six routes now reads from it. Chip, badge and label-group micro-spacing is deliberately
    left alone — the scale governs boxes and the gaps between components, not text stacking.
    - **The five page headers were five identical copies of one rule, and one of them had
      already drifted into a bug.** They now share the layout's `::deep .bar`, and only the
      window's own strip — newly marked `.titlebar` — reserves room for the OS overlay. It was
      applying that reservation, plus the titlebar's `min-height`, to every page header; the
      padding survived only because the pages' shorthand happened to be bundled *later*, which
      is source order deciding a layout question. Verified the step-15 fix survived the merge:
      the archive's filter input still reads `user-select: text` / `-webkit-app-region: no-drag`.
    - **Real inconsistencies closed, not just re-spelled:** `.sheet` was 46rem on three pages
      and 42rem on Settings; `.control` 15rem on the task form and 13rem in Settings; the two
      identical amber warning boxes had different padding; the board mixed px with everything
      else's rem; and the session view's page edges were **asymmetric** — 0.4rem left, 0.6rem
      right — so the terminal and rail sat off-centre in their own window.
    - ⚠️ **One regression caught by measuring rather than by eye.** Snapping the lane gutter up
      to 12px pushed five *detailed* columns 1px past the app's own 960px floor. The gutter is
      now 8px — the same step the lanes are padded with, so the space between two strips in
      neighbouring columns reads as one measure — and both densities fit at 960 with no
      horizontal scrollbar.
    - **Verified DOM-level on all six routes** (board, archive, task, session, timeline,
      settings): every header strip 8px/12px with its first control at x=12, every body
      16px/12px, every sheet 736px, the card tabs' first tab also at x=12, and the session
      view's left edge, inter-pane gap and right edge all 8px. 723 tests green.
      **Not looked at in a real window** — the pane composites no frames, so a screenshot times
      out; that glance belongs with the type and theming passes.
  - [x] **Type — swept 2026-08-02.** `--act-t1`…`--act-t7` (9/10/11/12/13/14/20px) plus
    `--act-w-em`, `--act-track-caps`, `--act-lh-dense` and `--act-icon`. **Sixteen distinct sizes
    in two units became seven**, and the audit that proves it is per route: every text node under
    `main` on all six pages now computes to a scale value and nothing else.
    - **px, not the rem the spacing scale uses** — deliberate, and stated in the token block. The
      app is a fixed-density readout and its sizes were being hand-tuned to the half pixel
      (9.5px, 11.5px, 12.5px all existed); that is a pixel grid asking to be named.
    - ⚠️ **The hierarchy was inverted, and only measuring found it.** Radzen's Body2 renders at
      **14px** while ACT's page titles were 13px — so on the settings page every *field label* was
      larger than the *name of the page*. Fixed by making the top of ACT's scale Radzen's own
      (`t4` = its Caption at 12px, `t6` = its Body2 at 14px) and giving a page title `t6` +
      `--act-w-em`: it wins on weight, which is the only step a strip that dense can afford.
      - **And a first reading of this was wrong in a way worth recording.** Querying
        `.rz-text-caption` returned 11px — but the first match in the document is the top bar's
        `.fullname`, which *overrides* it. Radzen's Caption is 12px. When probing a framework's
        scale, query an element the app has not restyled.
    - **What the sizes now encode:** a card's title is **one size wherever a card is listed**
      (strip, archive row, follow-ups dialog) — it was 12.5px on the strip, 13.1px in an archive
      row and 12.8px in the dialog, three sizes for one datum, and the card number was three more.
      A page's title is the step above. Icons follow their own rule: beside a label they take
      `--act-icon` (the strip's Launch glyph was 16px next to its own 12px word), while icon-only
      buttons keep Radzen's, which already tracks the button size.
    - **One weight retired:** 500 existed once, on the error page. 600 is now the only emphasis;
      700 stays on the wordmark alone, as a logotype.
    - ⚠️ **A live bug the pass surfaced: `NotFound.razor` was rendering completely unstyled.** It
      reuses Error's `.errpage` markup, but scoped CSS stops at the component that owns it — so
      that page had a 32px browser-default heading, no padding and no colour, and had presumably
      looked that way since it was written. `Error.razor.css` is now a global block in `app.css`
      and both pages get it. Verified: `/not-found` reads 20px/600 in ACT's red with 24px padding.
    - **Verified per route and 723 tests green.** Board 9/10/12/13; archive 10/11/12/13/14;
      settings 12/14; task 9/11/12/14; session 9/10/11/12/14; timeline 9/11/14 — **no off-scale
      value on any page**. Same caveat as the spacing pass: DOM-level, not looked at in a real
      window.
  - [x] **Radzen theming — swept 2026-08-02.** The widgets had been shipping in Radzen's own
    palette: neutral grey cards on ACT's blue-grey control room, 4px corners next to ACT's 7px,
    and a generic `#3871ff` primary that appears nowhere else in the app. Now **one block of
    ~25 `--rz-*` declarations in `app.css`, written entirely in `--act-*` tokens**.
    - **Why one block covers both themes.** Radzen's variables are `var()` chains, not baked
      literals — `--rz-card-background-color` *is* `var(--rz-base-background-color)`,
      `--rz-card-border-radius` *is* `var(--rz-border-radius)` — so re-pointing the semantic layer
      cascades through every widget, and pointing it at tokens that already switch means the theme
      switch is free. **Remapping Radzen's `--rz-base-50…900` ramp was the alternative and was
      rejected:** the ramp runs lightest-to-darkest in *both* sheets and only the semantic pointers
      flip, so one ramp slot means "page background" in one theme and "body text" in the other —
      three copies to maintain, to say what fifteen lines say once. Verified by flipping the app to
      light at runtime and re-reading every mapped variable.
    - **The overrides had to move to `:root`, and the theme attribute with them.** They are written
      in `--act-*`, and those lived on `.act-root` — but Radzen mounts a dialog's mask and wrapper
      at `<body>`, outside it. `data-act-theme` is now stamped on `<html>` (App.razor for the first
      paint, `actTheme.apply` after), the token blocks are `:root`, and a probe appended to `<body>`
      resolves both `--act-surface` and `--rz-dialog-background-color`. *Dropdown panels turned out
      to render inline* — the long-standing comment in `app.css` naming them as body-appended is
      only right about dialogs.
    - ⚠️ **An accessibility bug the swap created, and the one assumption in Radzen's palette that
      does not survive it.** `--rz-on-primary` is a literal `#ffffff`, which holds only while every
      accent is dark. ACT's dark accents are *light* — they are built to glow on a near-black
      board — so the Save button came out white-on-`#5aa0e6` at **2.79:1**, under AA. Fixed with a
      per-theme `--act-on-accent` (near-black in dark, white in light) mapped onto the whole
      `--rz-on-*` family: **6.92:1** measured after, and the value was picked by computing the ratio
      against all four accents rather than by eye.
    - ⚠️ **And a pre-existing one it exposed: three ACT badge rules were never reaching the
      screen.** `.colcount`, `.schip` and `.hiddenattn` each declare `color`, but Radzen's
      `.rz-variant-outlined.rz-badge-base.rz-shade-default` is one class heavier, so the framework's
      grey won every time — close enough to `--act-faint` that nobody caught it by eye. Found by
      walking the rendered DOM for neutral greys the palette does not contain. The selectors now
      carry `.rz-badge` to match its weight, and `--rz-base` (the accent behind every `Base`-styled
      control) maps to `--act-dim` — *not* `--act-faint`, which is 2.6:1 on a card and cannot carry
      a button label.
    - **Verified per route:** board, archive, task, session, timeline and settings each scanned for
      opaque neutral greys outside ACT's palette — **zero on all six**, in dark and in light. Cards,
      panels, inputs and dialogs on `--act-surface`, every corner 7px, status colours mapped to the
      board's own. 723 tests green, no server errors.
  - **User settings page** — consolidate the preferences that landed scattered
    across features into one screen: **theme** (Radzen *Standard* / *Standard
    Dark*; default **follows the OS** light/dark preference), density default,
    notification matrix, keep-awake, `maxConcurrent`, weekly-reset time,
    auto-archive window (**landed**), auto-execution pause. (Each knob works from its own
    feature step; this just gives them a home. The theme override replaces the
    interim OS-only auto-switch wired at Radzen setup.)

## Phase 7 — Spawning & lineage (the last step)

**Moved here on 2026-08-01** — it used to sit in Phase 4 as the step after completion, and it
is now the last thing ACT builds. **Its number stays 12**, because the number is an identifier
the spec cites (*Agent ↔ ACT contract*, *Local-endpoint security*) and renumbering four other
steps to close the gap would break those references for nothing. So the sequence ends
…15, 16, 12.

- [ ] **12. Spawning & lineage — an MCP server** — the `create_followup` tool and
  parent/children. *Verify:* a real agent calls the tool mid-session and a tracked child card
  appears in Ready with correct lineage, on both agents.
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

## Milestones

- ✅ **After step 10 — reached 2026-08-01.** Usable, manually-driven, **multi-agent** ACT (a
  plausible v1): every session runs in its own embedded terminal, the board tells you which one
  needs you, and you answer it there.
- ✅ **After step 14 — reached 2026-08-02.** The overnight unattended batch vision: a queue of
  Ready tasks launches itself within the cap, waits out a busy folder, a dependency or a spent
  quota, and holds the machine awake only while there is something to wait for.
- ✅ **After step 16 — reached 2026-08-02.** The full product: every surface driven by one scale
  for space, one for type and one for colour — Radzen's widgets included, rather than sitting in
  their own palette beside them — and the settings page that gives the scattered knobs a home.
  *Polished* is claimed with the one caveat step 16 records: no pass made in a real window.
- **After step 12, which is now last** — agent-spawned follow-ups on top of it.

## Build-time items to verify (from the spec)

- **The hook-injection unknowns now live with step 8** — `--settings` and `type: "http"`,
  http-hook headers, Codex env expansion and `--profile` hook tables, and what
  `Notification` fires for. They each decide a fallback in that step's design, so they are
  listed there rather than duplicated here. The prototype's `PrototypeHookSettings` wrote
  both an `http` and a `command`/`curl` variant precisely because the first is unconfirmed.
- **ConPTY behaviour** — resize while the TUI is mid-render. ~~Bracketed paste~~ is no
  longer load-bearing anywhere: the prompt is a launch argument, and with send-back cut at
  step 10 ACT never pastes into a live session at all. The xterm sends the user's own
  keystrokes, paste included, and the TUI handles them as it would in any terminal.
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
  afterwards. ✅ **The id comes from the hook payload, as originally planned** — every one
  carries `session_id` *and* `transcript_path`, and `SessionEventSink.Bind` persists it onto
  the card. Verified live 2026-07-31.
  - **The detour, kept because the rollout layout is still worth knowing.** While the hooks
    were believed dead, ACT bound by *inferring* the file: sessions land in
    `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`, indexed by
    `~/.codex/session_index.jsonl` (`{id, thread_name, updated_at}`), and `CodexRolloutFinder`
    matched the newest whose `cwd` and start time fit the launch. That class is **deleted**;
    the rollout is still read, but only for enrichment, and only once a payload names it.
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

## Deferred — git worktree support

Let a card run in its own git worktree instead of the repository working
directory, so two cards on the same repo cannot fight over the same files.

- [ ] **Claude Code** — the CLI supports worktrees natively; ACT would mainly
  need to decide where the worktree lives, pass it as the session working
  directory, and clean it up when the card completes.
- [ ] **Codex** — no native support; requires an external wrapper
  (`agentree`, `nymbalyst`, or equivalent), which means an extra dependency ACT
  cannot assume is installed. Needs a probe + a clear "unavailable" path.

Deferred because the two adapters would diverge sharply: one is a working-
directory change, the other is a third-party tool dependency with its own
lifecycle. Revisit once the per-card working directory is a first-class
setting.
