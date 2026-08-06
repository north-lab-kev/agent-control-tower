# bUnit coverage — working plan

**Temporary working file — every phase is now ticked, so this can be deleted.** It existed so an
interrupted session could pick up where it stopped. The parts worth keeping have moved: the ground
rules and the harness are in `tests/README.md`, and the tree line in `repository-structure.md` names
the suites. What is left here is the record of what was covered and in what order.

Closes `docs/todo.md` item 2, *implement bunit tests*.

## Ground rules

These hold for every phase.

1. **Extraction beats rendering.** If a fact is `Card` + `now` in, strings out, it belongs in a pure
   class with a pure test — the way `StripFace` was pulled out of `FlightStrip`. A render test for
   what a switch expression returns is strictly worse than a plain one. When a page test starts
   wanting to assert a computed string, the answer is usually a new `Cards/` type, not a bigger
   bUnit file.
2. **Never re-test the pure layer through the DOM.** `StripFaceTests`, `TimelineEntryTests`,
   `UsageMetersTests`, `NewTaskFormTests`, `BoardStateTests` and the rest already own their subject.
   A bUnit test earns its place only by asserting one of:
   - a computed value reaching the element that consumes it (class, `title`, `href`, `disabled`),
   - a gesture reaching the right handler (including bubbling and `stopPropagation`),
   - conditional markup — which branch rendered, and what is *absent*,
   - an `EventCallback` contract between a parent and a child,
   - lifecycle: what a re-render, a parameter change, or a service event does.
3. **House style applies to tests.** Code-behind conventions do not (a test is not a component), but
   everything else does: constructor injection, no braces on a single-statement `if`, AwesomeAssertions
   (`Should()`), no comments except where the *why* is not in the name — and the existing suites'
   habit of a comment above a test that records the decision being pinned is worth keeping.
4. **Culture is pinned to `en`** by `ComponentTest`, because assertions read resource text.
5. **No real filesystem, no real process, no real store.** Same rule the rest of the project holds:
   everything goes through the existing fakes.
6. **Selectors come from the app's own classes**, never from Radzen's internals (`.rz-*`). Where a
   test needs a hook the markup does not have, add a class the CSS could plausibly want — do not add
   `data-testid`.

## Phase 0 — the harness

- [x] `bunit` 2.9.0 in `Directory.Packages.props` + `Act.App.UiTests.csproj`
- [x] `ComponentTest` — base `BunitContext`: `en` culture, `IClock`, `AddRadzenComponents()`, loose JSInterop
- [x] Swap `ComponentTest`'s clock to the project's own `FrozenClock` rather than a zero-step `TestClock`
- [x] `TestGraph` — one place that builds the app service graph over fakes, so a page test is a line
      or two. Registers: `BoardState` (loaded), `UserSettingsService`, `SessionRegistry`,
      `SessionLauncher`, `QueueRunner`, `CardCompleter`, `CardReopener`, `TerminalGeometry`,
      `TaskTitles`, `IAgentCapabilityCatalog`, `AttachmentOpener`, `UsageState`, `UiPresence`,
      `DeepLinkRouter`, `NotificationDispatcher`, `IDesktopBridge`, `IWorkingDirectories`,
      `IAttachmentStore`, `IExecutableProbe`, `ActLogLocation`, `IAssetVersions`, `ILogger<>`
- [x] Promote the duplicated `PassThroughDirectories` (two copies, in `SessionLaunchTests` and
      `SessionRestartTests`) into a shared `FakeWorkingDirectories`, and give it the knobs the picker
      and the task form need — a listing per path, a `Check` that can fail
- [x] `FakeDesktopBridge` (records `OpenExternalAsync` / `OpenPathAsync`, can refuse)
- [x] `FakeExecutableProbe` (nothing on PATH by default, per-name answers)
- [x] `NavigationTracker` helper — read the last url bUnit's fake `NavigationManager` was sent to

## Phase 1 — leaf components

Small, pure-ish, no page graph. These set the pattern for everything after.

- [x] `FlightStripTests` — **done in the pilot.** 7 tests: face → element, hold tooltip on the
      wrapper, `draggable` per column, click opens, keyboard opens on Enter/Space only, launch does
      not bubble into open, compact vs detailed
- [x] `CardTabsTests` — three tabs, one `href` each, `Route` agrees with what is rendered, the
      active tab is the one matching the current url
- [x] `CardFilterTests` — typing raises `QueryChanged` once, Escape clears, the clear button clears,
      re-setting the same query raises nothing, the clear affordance is absent while empty
- [x] `AttachmentPreviewTests` — `Id` on the element `act-attach.place` looks up, `Url`/`Alt` land,
      starts hidden
- [x] `ArchiveFollowUpsDialogTests` — the count reaches the question, three buttons close with three
      distinct `FollowUpChoice` values
- [x] `TemplateNameDialogTests` — suggestion pre-fills, Enter accepts a named template, Enter on a
      blank one does nothing, Cancel closes with null, the trim
- [x] `PathPickerTests` — opens at `NearestDirectory(StartAt)`, walks into a child, the up entry,
      directory mode confirms with *Use this folder* while file mode picks on click, each `PathError`
      renders its own wording
- [x] `UsageIndicatorTests` — a meter per enabled agent, a notice for an unavailable one, a
      `UsageState.Changed` re-renders, a settings change drops a switched-off agent's meter

## Phase 2 — the board

- [x] `BoardViewTests`
  - a lane per column, each header counting its cards
  - the filter narrows lanes and the header reads `shown/total`
  - a lane's hidden-attention marker appears only while filtering
  - the archive link carries the query, and shows only when the archive matches
  - auto-exec chip: icon, class and tooltip both ways, clicking it toggles the setting
  - density chip names the mode you are in, clicking it flips the setting
  - a strip per card, wired to the board's own handlers
  - clicking a Preparing/Ready card navigates to `/card/{id}/edit`; a launched one to its terminal
  - Launch on a Preparing card promotes it to Ready first, then launches
  - a launch that reports a message notifies, and `Waiting` picks the warning severity
  - the double-click guard: a second launch while one is in flight is dropped
  - Retry appears only for a card the launcher says is retriable
  - a hold from `QueueRunner.Latest` reaches the strip that owns it
  - `board.Changed` / `queue.Evaluated` / `settings.Changed` each re-render
  - the new-task split button: plain click → `/card/new`, a template item → `?template={id}`,
    and the default template is not offered in the menu

## Phase 3 — the task form

The biggest surface in the app, and the one where a regression costs the most.

- [x] `TaskViewTests` — create/edit shape
  - `/card/new` renders the create heading and the default template's values
  - `?template={id}` starts from that template; an unknown id falls back to the default
  - an edit loads the card, and the heading is the card's title on the *first* render
  - an id that is not there renders the not-found branch and no form
- [x] `TaskViewLockTests` — the two read-only levels
  - an archived card renders every field disabled and offers no Save, no Delete
  - a launched card locks the launch inputs but leaves the rest editable (`TaskEditing`'s split)
  - a locked card renders no attachment remove button and no drop zone, but still lists its files
- [x] `TaskViewChoicesTests` — the dropdown cascades
  - agent list is the enabled agents, plus the card's own when it was switched off
  - switching agent drops a model the new one does not offer, and an effort with it
  - switching agent resets an unsupported permission mode to `Default`
  - switching model drops an effort the new model does not offer
  - a stored model/effort/mode the agent no longer offers stays in its list
  - efforts are offered against the agent's default model while the model box is blank
  - the schedule box shows the datetime field only for the schedules that want one
  - PR-only draft: choosing another git action clears `Draft`
  - the unattended warning appears for a scheduled task with a prompting permission mode
- [x] `TaskViewValidationTests`
  - a malformed working directory blocks Save and says which way it is malformed
  - a merely missing directory does not block, and offers to create it
  - the missing-directory text names the resolved path only when it differs from the typed one
  - Save is refused with neither title nor prompt, and permitted with only a prompt
- [x] `TaskViewAttachmentTests`
  - a file over the per-task cap refuses the whole selection rather than truncating
  - an oversized file is refused by name and the rest of the selection still lands
  - a removal is in-memory until Save, and Save prunes to the names that survived
  - a locked card is never pruned
  - the row's icon and label change for a file whose bytes are gone
  - the hover preview renders only for an image that is still there
- [x] `TaskViewExitTests`
  - a dirty form asks before it navigates, and a refusal keeps the page
  - a clean form leaves silently
  - Save and Delete both leave without asking
  - Escape closes an open picker first, and only then leaves
  - a card with follow-ups asks what happens to them; each choice does what it says
  - Duplicate copies the *saved* card and opens the copy
  - Save as template captures the *form* and needs a name

## Phase 4 — the terminal view

- [x] `SessionViewTests`
  - an archived card renders the empty statement and no terminal frame
  - the action rail offers exactly what the rules allow — launch / complete / reopen / restart
  - the handoff link appears only when the adapter answers with one
  - Complete leaves for the board; Reopen stays on the page
  - a restart clears the xterm before rebinding
  - `board.Changed` swaps in the store's new card instance
  - the drop rail: no session refuses the drop with a warning, a live one writes quoted paths
    with a trailing space and never a submit key
  - attachments dropped mid-session do not join the card's own list

## Phase 5 — the remaining pages

- [x] `SettingsViewTests`
  - every switch writes through on change, with no Save button anywhere
  - a section per registered adapter, in registration order
  - the binary check's four states each render their own wording, and *use this one* only where
    there is a path to adopt
  - a language change persists and forces the reload; the same language does nothing
  - the retention day box is held between `CompletedRetention`'s bounds
  - desktop-only rows are absent in browser mode
  - the log folder row reports a refusal rather than failing silently
- [x] `TemplateViewTests` — the same cascade rules as the task form, no title field, no datetime,
      Save persists and returns to the list, a stale id is the not-found branch
- [x] `TemplatesViewTests` — a row per template with its meta line, delete asks first, the default
      template is marked and cannot be deleted
- [x] `ArchiveViewTests` — a row per archived card, the `q` query seeds the box once, restore and
      duplicate, purge asks and names the count
- [x] `TimelineViewTests` — a row per transition with the badge that reported it, the rail, an empty
      card says so, `board.Changed` follows

## Phase 6 — the shell

- [x] `MainLayoutTests` — the top bar's three destinations, the theme attribute follows the setting,
      density and blink are cascaded down, presence is reported on navigation and forgotten on
      dispose, a deep link navigates
- [x] `ErrorPageTests` — the request id shows only when there is one

- **Note (Phase 2).** Two harness facts worth keeping: `ComponentTest` implements `IAsyncLifetime`
  because `SessionRegistry` and `QueueRunner` are `IAsyncDisposable`-only and the container refuses a
  synchronous teardown while it owns one; and a page under test renders no `RadzenComponents`, so what
  it *reports* is read off `NotificationService.Messages` rather than found in the markup.

## Log

- **2026-08-05** — pilot landed: `bunit` 2.9.0, `ComponentTest`, `FlightStripTests` (10 cases,
  423 ms). Docs updated (`tests/README.md`, `repository-structure.md`). Full project green at 297.
- **2026-08-05** — Phase 0 + Phase 1 landed. Harness: `ComponentTest` now builds the whole app graph
  over fakes (+ `FakeWorkingDirectories`, `FakeDesktopBridge`, `FakeExecutableProbe`,
  `FakeAssetVersions`, an `OpenDialog` helper that drives a real Radzen dialog). `InternalsVisibleTo`
  added to `Act.App` so the tests can use the app's own `AgentCapabilityCatalog` rather than a
  re-implementation. Seven leaf suites: 297 → 350 tests, 392 ms.
- **2026-08-05** — Phase 2 (`BoardViewTests`, 27) → 377.
- **2026-08-05** — Phase 3, the task form: six suites (`TaskView`, `Lock`, `Choices`, `Validation`,
  `Attachment`, `Exit`), 89 cases → 466. `ComponentTest.NarrowerCapabilities` was added here — the
  second mock adapter is deliberately narrower than the first, because two adapters with identical
  capabilities can never show that switching agent drops a model the new one does not offer.
- **2026-08-05** — Phase 4 (`SessionViewTests`, 27) → 493.
- **2026-08-05** — Phase 5: settings, template editor, templates list, archive, timeline — five
  suites, 94 cases → 587.
- **2026-08-05** — Phase 6 (`MainLayoutTests`, `ErrorPageTests`, 20) → **607**. Whole solution green
  at 1523 across four test projects; `Act.App.UiTests` runs in ~3 s.

## What is deliberately not covered

Not gaps to fill later — things bUnit structurally cannot see, kept here so nobody goes looking:

- **The five JS interop modules** (`act-terminal`, `act-attach`, `act-escape`, `act-presence`,
  `act-unsaved`). bUnit stubs JS, so what a test can assert is that the page *asked* — the module was
  imported, `actTheme.apply` was invoked — never what the module then did. This is the Playwright
  case, and the only strong one.
- **xterm.js.** It needs a real window to paint; see the note in the session-view suite.
- **CSS.** The tests read class names, never computed styles — so the flight-strip look, the themes
  and every layout rule are unverified here by design.
- **The held navigation resuming after a discard.** `TaskViewExitTests` pins that discarding writes
  nothing; whether bUnit's navigation manager then re-runs the held navigation is a bUnit detail
  rather than a promise this app makes.
