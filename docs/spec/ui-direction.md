# UI direction

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

- **Aesthetic:** **air-traffic control room.** Cards are **flight-progress
  strips** — colored status-light rail on the left edge, callsign-style id +
  working-dir path in mono, title in a technical sans. Control-room palette —
  dark is the signature look (deep blue-slate bg), with a light variant for the
  OS light setting (see Theme below); semantic status colors are the same in
  both: teal=running, amber=needs permission/answer, red=error,
  blue=idle/to-review, muted green=done, dim yellow=quiet. Brand mark = a small
  radar sweep.
- **Launch is "Launch now".** Named for the override it is: a card can be waiting
  on a `schedule`, and this button starts it regardless. Plain "Launch" would read
  as a different action from what the button does on a scheduled card.
- **A strip can be deleted from the board**, by a quiet icon button last on its header
  line — the same place in both densities, so the gesture does not move when the board's
  density does. It is the one action on a strip that takes work away, so it is a text
  button at reduced opacity that takes `--act-err` on hover, never an outlined button
  competing with *Launch now*. Nested with the badges rather than loose on the header row,
  because that row is `SpaceBetween` and a third loose child would push the badge into the
  middle of the strip.
  - **It asks first only where an agent is live** — **Executing** and **Your turn** — see
    `CardDeleteConfirm`. Deleting is a soft archive and restorable, so an "are you sure" on
    every strip would be ceremony on the gesture used to tidy up; but those two columns end a
    running process, and that is the part the archive cannot put back. The question names the
    card: on a board of a dozen strips, "are you sure" beside the wrong one is how the wrong
    agent gets stopped.
  - **The task page's own delete obeys the same rule**, through the same `CardDeletePrompt` — the
    board asking and the task page going quietly was a real gap, not a difference of surface. The
    rule check lives inside the prompt so a caller cannot take the dialog and forget the rule.
    One consequence worth knowing: a **Your turn** card is the normal review-then-file case, so
    signing off and archiving now costs one confirmation it did not before.
  - **Follow-ups get the same dialog the task page opens**, because what happens to them is a
    genuine choice rather than an "are you sure" — and a card whose children silently outlived
    it is the orphan the board exists to make hard to create. The kill-then-delete ordering is
    shared with the task page in `CardDeletion`; a second copy of it is how one of the two
    eventually loses it.
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
  The board background is a flat `--act-bg`: a fixed-pitch grid never aligns
  with flexible column widths, so it would read as noise rather than structure.
- **Density toggle:** **Compact** (id + title + **badge** + schedule chip — badges
  shown, not just a dot — then a second line carrying the **working directory** and
  the **launch / retry** button as an icon) vs **Detailed** (fuller strip with
  metrics, path, lineage, labelled buttons). Chosen in *Settings → Appearance →
  display mode* **or on the board**, on a chip in the control row beside the
  auto-execution switch, and persisted either way. In compact the title ellipsizes
  so the badge keeps its full width: the badge is the signal, the title yields.
  - **The path and the launch button survive compact.** Compact is the density you
    scan a full board in, and it is *from* that board that work gets started: the
    folder says which checkout a card is about, and the button is the whole point
    of the glance. Both sit on a second line of their own, so the first line's
    badge keeps exactly the width it had; the button drops its label and the path
    ellipsizes, which is where the density is paid for.
  - **A spawned card says so in both densities.** A card an agent
    created is not one the user wrote, and telling them apart at a glance is the point
    of recording `parentId` at all. Detailed carries it in the lineage area as
    `↳ #1039`; **compact carries it too**, on the second line the path and the launch
    button already share, so the badge keeps its width and the title keeps its
    ellipsis. If it will not fit there at the 210px minimum column, the strip gets a
    little taller rather than the marker getting dropped.
  - **The chip names the mode you are in, not the one you would get.** The board in
    front of you is the answer to "which mode is this", and a button labelled with
    the *other* mode would contradict it on every glance.
  - **"Detailed", not "Spacious".** The setting's own hint says *how much detail
    each flight strip shows*, so the label says the same thing; "spacious" would
    describe the whitespace, the side effect rather than the point. Renaming a
    persisted enum value is a store change, never only a rename — see
    `design-notes.md` → *Renaming a persisted enum value*.
- **Attention:** needs-you cards get a **loud in-place treatment** (glowing rail
  + pulse + `!` corner); no separate inbox, and no top-bar counter either — a
  **"N need you ›"** pill could only ever count cards and point at the board it
  sat above, since ACT answers no permission prompt. The card is the signal — the
  strip is already loud, and the columns already group by state.
- **Interaction — two surfaces, one rule.** The **drawer** is where you *read* a
  card; the **session view** is where you *talk* to it.
  - **Contextual right-side drawer** over a dimmed board, action set by state.
    Actions: Open terminal, Open in Desktop, Restart terminal, **Retry** (± edit launch
    config, on `error`), Complete. The first three on any active card.
    - **No Send back.** Replying to finished work is typing into
      the session, and the session is a tab on the card. A compose box in the drawer or
      the rail is a worse text input inches from a better one, and ACT types nothing
      on the user's behalf; see *The input channel is the human at
      the keyboard*, which is absolute.
    - **The badge is the whole report.** A blocked card deliberately carries no
      read-only statement of what is being asked: `needs permission` /
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
  title, prompt, dir, agent, model, effort, permission mode, schedule — and
  a **single Save** → lands in Preparing; the user drags it onward. The fields sit in a
  centred column so a wide window does not stretch them.
  - **There is no *Advanced* section any more.** It held five fields: the tool allow/deny
    pair, deleted for being silently dropped by one of the two agents, and the executable /
    flags / environment, which moved to *Settings → Agents* because they describe the
    install rather than the work. Nothing was left to collapse.
  - **Past the launch boundary the form gates itself** (`TaskEditing`): prompt, dir, agent
    and schedule freeze — they describe the run that already started — while
    the title and the launch config stay editable, since those are what the *next* launch
    uses. Enforced in the code that writes the card, not only in the markup, so the
    immutable-`initialPrompt` rule holds however the form is rendered. One notice at the
    top says why, once.
  - **Why a page:** a card is then always somewhere you can land, link to, and come
    back from, and its two faces — the form before launch, the terminal after — are
    the same kind of thing rather than one modal and one route. Clicking a card opens
    whichever face applies: the form in Preparing / Ready, the terminal past the
    launch boundary. **Every surface is a page** — the board, the task form, the
    terminal, the archive, the templates and settings.
  - **The title strip never dims.** The OS draws the window buttons *above* all web content, so
    a mask over the whole window dims everything except them and leaves them stranded in a bright
    block. Nothing in CSS can reach them, so the mask stops below the strip instead and the strip
    stays lit as a unit — the seam cannot form because no boundary runs through it. Its controls
    go inert while a dialog is up (navigating away would strand the dialog); the native buttons
    stay live, because closing the window must always work.
  - **Dialogs are for forks, not for places.** A page is somewhere you go and can link to;
    a dialog interrupts an action already under way and has no meaning on its own. So the
    modals are the two destructive prompts — archiving a card with follow-ups, and
    emptying the archive — plus **naming a template** on the way out of *Save as a template*,
    which is the same shape without being destructive: it interrupts a save already under way,
    and there is no url that means "name the template I am about to make". The completion/git
    prompt that was to be the third was cut along with every other git mechanism — a git action
    is a sentence in the prompt — so sign-off is a plain drag. Anything that is a
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
  - **Spacing is a scale, not a per-component guess.** `--act-s1`…`--act-s6`
    (4/8/12/16/24/32px) in `app.css` beside the colour tokens, plus four measurements
    every page repeats — `--act-bar-pad`, `--act-page-pad`, `--act-sheet-max`,
    `--act-control-w`. It governs **box padding and the gaps between components**; the
    sub-pixel stacking inside a single label group, and the chrome of a chip or a badge,
    stay where they were tuned. The payoff is that the header strip and the body under it
    share a horizontal edge, so a page header's first control lines up with the content
    below it on every page — and that the one *page header* rule lives in the layout
    (`::deep .bar`) rather than in five identical copies, with only the window's own strip
    (`.titlebar`) reserving room for the OS overlay.
  - **Type is a scale too, and it interlocks with Radzen's.** `--act-t1`…`--act-t7`
    (9/10/11/12/13/14/20px), in **px** rather than the spacing scale's rem: this is a
    fixed-density readout — a 960px floor, a terminal on a character grid — and its sizes were
    being hand-tuned to the half pixel. Two steps are deliberately Radzen's own (`t4` = its
    Caption, `t6` = its Body2 and controls), so ACT's chrome steps *down* from the widgets
    instead of landing a pixel off them. One emphasis weight (`--act-w-em: 600`; 700 is the
    wordmark's alone, as a logotype), one tracking for uppercase labels
    (`--act-track-caps`), and one dense leading (`--act-lh-dense`).
    - **The hierarchy rule the sizes encode:** a **card's title is one size wherever a card is
      listed** — flight strip, archive row, follow-ups dialog — while a **page's own title is a
      step above at `t6` + `--act-w-em`, because it has to out-rank the labels it heads.
      Icons are sized as icons: an icon beside a label takes `--act-icon` so it never out-sizes
      its own word; an icon-only button keeps Radzen's, which already tracks the button size.
  - **"Radzen themed to the same palette" is one block of `--rz-*` in `app.css`, written in
    `--act-*`.** Radzen's variables are `var()` chains rather than baked literals, so re-pointing
    the *semantic* layer — `--rz-base-background-color`, the text and border families,
    `--rz-border-radius`, `--rz-base` and the status colours — cascades to every widget, and
    pointing it at tokens that already switch means **one block serves both themes**. Remapping
    Radzen's `--rz-base-50…900` ramp instead was rejected: the ramp runs the same direction in both
    sheets and only the semantic pointers flip, so a slot means "page background" in one theme and
    "body text" in the other.
    - **The tokens therefore live on `:root`, keyed by `data-act-theme` on `<html>`**, not on the
      layout root — Radzen mounts a dialog's mask and wrapper at `<body>`, outside the layout, and
      the mapping has to reach there.
    - **`--act-on-accent` exists because Radzen's `--rz-on-*` are a literal white**, which holds
      only while every accent is dark. ACT's dark accents are light — built to glow on a near-black
      board — so white on them fails AA. The token is a near-black in dark and white in light,
      chosen by computing the ratio against all four status colours.
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
  gear in the top bar and grouped by type (**General**, **Retention**, **Appearance**,
  **System**, more to come).
  Every setting applies immediately and persists to LiteDB — no OK/Cancel, and no Close
  either: the only action is the back arrow. A page rather than a dialog for the same
  reason as the task form — see *Interaction* — and a page also survives the reload a
  language change forces, where a dialog would be dismissed as a side effect and dump
  you on the board.
  Shipped: **language**, **write titles from the prompt** (*General*),
  **archive completed tasks automatically** with its window in days,
  **theme** (follow-OS / light / dark override),
  **display mode** (Compact / Detailed — also a chip on the board's control row),
  **blink in Your turn**, **keep-awake**, **close-to-tray**,
  **pause automatic execution** and **tasks running at once** (*Execution*),
  **Agents** — one section per registered adapter carrying that CLI's **enabled** switch,
  executable, extra flags and environment (see *Launch config*; these are properties of
  the install, which is why they are here and not on the task form),
  **help improve ACT** (*Diagnostics*). Still to land:
  Weekly-reset time.
  - **Task defaults live in *Task templates***, a managed list on its own page (see
    *Task templates*). What Settings holds is a **pointer** to that page rather than a
    second place to edit the same thing — a setting the user goes looking for in Settings
    should still be findable there.
  - **Desktop notifications** (*System*, on by default, **desktop only**) is a single
    switch — see *Native OS notifications* for why there is no per-event matrix. It is
    hidden in browser mode for the same reason close-to-tray is: a switch that governs nothing
    is worse than no switch.
  - **Archive completed tasks automatically** (*Retention*, on by default, **10 days**) is the
    retention policy's one knob, and the day count only appears while the switch is on — a number
    that governs nothing is a question the user has already answered. The window is clamped to
    1–365 days on the way in, because the store is written on every keystroke's `Change` and a
    typed `0` must not mean "archive everything the moment it is signed off". See *Data
    retention* for what the sweep does and what it refuses to touch.
  - **Write titles from the prompt** (*General*, on by default) is the switch over the one thing ACT
    does that spends the user's quota without being asked — see *Auto-generated titles* in the
    *Task / card data model* for what it governs and what an untitled card then looks like. The
    **wand on the task form is unaffected**: the switch is about what ACT does on its own account,
    and a click there is the user asking.
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
  - **Close to the notification area** (*System*, desktop-only, on by default) makes the
    window's close button leave ACT running behind a tray icon. The window is genuinely
    **destroyed and rebuilt**, not hidden: Electron.NET's `close` handler never calls
    `preventDefault`, so a close cannot be cancelled from C#. That costs a page load on the
    way back and nothing else — sessions belong to the registry, not to a view, so agents
    keep working across the gap and the terminal re-attaches. The switch is hidden in browser
    mode, where there is no window to close and no tray to close it into.
    - **The tray tooltip carries the waiting count** — `Agent Control Tower — 3 tasks
      waiting for you`, with a **separate singular wording** per language (`1 task waiting
      for you`) rather than a `task(s)` that reads as unfinished. It falls back to the bare
      name when nothing is waiting, because a zero in a tooltip reads as a number worth
      checking — which is also what spares the plural from having to answer for zero, where
      English and French disagree. Counted with `NotificationTrigger`
      rather than by column, so the tooltip, the toast and the blink can never disagree about
      what "the ball is in your court" means. **Not `app.setBadgeCount`:** that is macOS and
      Linux-Unity only, its Windows counterpart `setOverlayIcon` is not in the Electron.NET
      bridge, and with close-to-tray on there is no taskbar button to hang an overlay from
      anyway — the tray icon is the only surface that exists at the moment the count matters.
    - **Exit lives on the tray icon, behind a confirmation** that names what is lost: the
      running tasks it will stop (with a count, when there are any) and the scheduled tasks
      that will not run while ACT is closed. It is a **native message box**, because the
      window it would otherwise open in is usually the one just closed. Electron shows
      nothing for a parentless message box, so choosing Exit with no window brings the window
      back to carry the question — being shown what is about to stop is no bad thing.
      Confirming ends every live session first: the confirmation promised it, and an agent
      must not outlive the app supervising it.
  - **Check for updates** (*Updates*, desktop-only) is a **switch**, on by default. On, ACT checks every
    six hours and downloads what it finds. Off, nothing reaches the network unasked — and *Check now*
    still works, offering a *Download* button for whatever it turns up.
    - **It was a three-way and is not any more.** The retired middle position, "tell me, download when I
      ask", existed for the metered connection that cannot afford an unasked hundred megabytes. That
      user is still served: switch off, check when they choose, download deliberately. The only position
      genuinely lost is being told about a version automatically *without* it being fetched, which is
      narrower than a whole third choice deserves. The collapse needed a schema migration — see
      `docs/design-notes.md`, "Releasing".
    - **The switch governs ACT's own initiative, never what the user may ask for.** *Check now* is live
      either way, and a live button that did nothing is the bug that made this rule explicit. The
      section also states **the running version**, in both shells and unconditionally — it is
    the one thing a bug report has to quote and ACT stated it nowhere until now.
    - **A downloaded update offers *Restart and install*, on the board as well as in Settings.** It sits
      last in the title bar's action group — after the three destinations, so a release never moves them
      under the user — and carries its version as text, because an icon cannot say that a different
      version is waiting. It is offered under **all three policies**: by the time the installer is on
      disk the choice the policy governed is already made, and what is left is a restart only the user
      can time. It ends the live sessions and asks about running work through the same confirmation as
      the tray's *Exit* — a restart is an exit that comes back, and a title-bar button that stopped
      three agents without asking would be the worst button in the app.
      - **Why it exists.** An install started on the way out cannot report anything, because the app
        that would report it is the app being replaced. So install-on-exit reads as ACT going quiet for
        an indefinite while, and relaunching by hand too early finds shortcuts the installer has not
        finished rewriting. A restart the user asked for can answer: **the app coming back is the
        completion signal**, and there is nothing to click in the meantime.
    - **Applied only when the user says so.** ACT supervises long-running agents, so an update that
      restarted the app to install itself would stop work the user did not agree to stop — and an
      update that installed as they left would replace their app at the one moment ACT cannot tell
      them anything. *Restart and install* is the whole of it: **leaving never installs**, whichever
      way they leave, so the tray *Exit* is an ordinary exit and its confirmation says nothing about
      the update. `AutoInstallOnAppQuit` is **off**, and there is no install-on-exit call left to
      reach.
      - **Declining costs nothing.** The installer stays in electron-updater's pending cache, so a
        run that never clicks the button leaves it for the next one, which offers it again without
        downloading it again. A downloaded update may wait as many runs as the user likes.
    - **A check that fails is not an error.** Offline, a 404, a feed that does not exist yet: all of
      them read as *Could not reach the update feed. ACT will try again later*, never as *up to
      date*. Collapsing the two is the lie that leaves someone on an old build believing otherwise,
      which is why `UpdateCheck` carries `Completed` separately from the version.
  - **Help improve ACT** (*Diagnostics*, on by default) is the opt-out for anonymous usage
    data and crash reports sent to a cloud telemetry service. **On** by default, because a
    solo-maintained tool learns what to fix from the installs it never sees; one switch
    rather than a matrix, for the same reason notifications are one switch. In
    *Diagnostics* beside the log folder, since that is where a user already goes to answer
    "what does ACT know about my run".
    - **The sink is PostHog, behind a port.** `ITelemetrySink` is the interface; the vendor
      lives in `Act.Infrastructure/Telemetry/` and is named nowhere else (a test enforces it,
      the same way one holds Serilog to `Logging/`). One `"Telemetry"` configuration section
      owns the switch, the project token and the host, and **both** the switch and a token
      are required before any client exists — without them the sink is `NullTelemetrySink`,
      which is what makes a fresh clone and the test suite silent by construction.
      `appsettings.Development.json` turns it off, so a dev run never reaches the project.
    - **The key is stamped by the release, never committed.** `appsettings.json` ships an
      empty token; the pipeline writes the real one from a GitHub secret at package time,
      base64-encoded so it is not in clear in the installer, and the app accepts either
      encoding (`phc_…` marks the plain form). The encoding is convenience, **not**
      protection — a project key is write-only by design and safe to ship, and nothing
      genuinely secret may travel this path. See *The token is encoded, and that is not
      security* in `docs/design-notes.md`.
    - **What is sendable is a single file.** `TelemetryEvents` builds every payload; call
      sites pass typed values and never a property bag. Three events: the run starting —
      carrying the settings it ran under, in that same event — a launch, and a crash. Under it
      `TelemetryPayload` drops any event name, property key or value shape that was not
      declared, so the setting's promise (never prompts, task text, titles, file paths or
      agent output) is enforced rather than intended. Shutdown sends nothing of its own; it
      only flushes what the run already queued.
    - **A crash report says where, never what.** It carries the *innermost* exception type
      (an unobserved task hands over an `AggregateException`, and the wrapper is the wrong
      thing to group by), the wrapper chain when there was one, and frames as
      `Namespace.Type.Method (File.cs:42)`. The **message never leaves** — it is runtime data
      from the user's machine and in this app it routinely names the file that failed. The
      file name and line do, because they come from ACT's own PDB and describe this
      repository, not the user's disk; base name only, never an absolute path.
    - **Events are the quota, so they are spent carefully.** An analytics plan meters events
      ingested rather than payload size, which is why the settings ride on `app_started`
      instead of a `settings_snapshot` of their own: two events cost twice as much to say one
      thing. It also reads better — a run can be broken down by any setting without joining
      two events together.
    - **The scope is the app, not the work.** These answer what ACT is used *with* and where
      it breaks — never how a given card went. There is deliberately no `task_completed`
      carrying turn counts, tool calls, token totals and compactions:
      that measures the agent's work rather than the app's use of it, and it is the kind of
      per-run detail this switch should not be buying. `task_launched` stays, because which
      agent, and whether a schedule or attachments were involved, are facts about
      how ACT's own features get used.
      - **Launches only, never resumes.** A restore or a terminal restart sends nothing:
        `SessionRestorer` replays one per live card on every startup, so counting them would
        report a card as started several times over and bury the real launches underneath
        restarts. A **retry** does count — the launcher claims the card and moves it to
        Executing, so it is a start the user asked for rather than a re-attach ACT performed.
    - **The opt-out has two gates.** The switch is read on *every* capture, not once at
      startup, so turning it off stops the next event and turning it back on needs no
      restart. Because the client also holds a batch of its own, the vendor's `BeforeSend`
      hook is wired to the same predicate — off means nothing leaves, queued or not.
    - **Install ID** (*Diagnostics*, read-only) is the anonymous `distinct_id` the data is
      grouped under: a random GUID, never machine-derived, seeded on first launch and never
      rewritten. It is shown beside the log folder and stored even when telemetry is off,
      because it is also the id a bug report quotes.
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
  - **A counted phrase ships as a `_One`/`_Many` pair**, picked by `Text.Plural`.
    `task(s)` reads as a string nobody finished, and French is worse off than English
    for it — the count reaches the adjective and the verb too (`1 tâche en cours
    **sera arrêtée**` against `2 tâches en cours **seront arrêtées**`), so a
    parenthetical `(s)` cannot be right there at any count.
  - **Singular at one only, and zero belongs to the caller.** English wants
    "0 tasks" where French wants "0 tâche", so a shared rule would be wrong in one of
    them; every caller establishes the count is non-zero first, because a phrase
    counting nothing is a phrase not worth showing.
  - A language that does not inflect keeps both halves anyway — `Board_HiddenAttention`
    is `{0} hidden` twice in English and `masquée`/`masquées` in French. The pair is a
    property of the *key*, not of one language, and `TextPluralTests` sweeps the resx
    to pin it: every `_One` has a `_Many`, both resolve in every shipped language,
    each half carries the same placeholders as its translation, and no singular
    reaches for an argument its plural never names.
