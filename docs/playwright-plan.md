# Playwright coverage — working plan

**Temporary working file**, same as `docs/bunit-plan.md` was: delete it once the last phase is
ticked, and move anything durable into `tests/README.md`.

Picks up where the bUnit work stopped. That tier now covers all 21 components (607 tests, ~3 s), so
this one is **not** "the same tests in a browser" — it is the short list of things bUnit
*structurally cannot see*. Keeping it short is the point: every spec here costs a browser, a server
and a few seconds, and this is a solo project.

## What justifies the tier

Exactly three things, in descending order of value.

1. **The five JS interop modules — 550 lines with zero automated coverage.** bUnit stubs JS
   entirely: a component test can assert that the page *asked* (`actTheme.apply` was invoked, the
   module was imported), never what the module then did. `act-attach` alone is 214 lines of measured,
   subtle behaviour — the clipboard rules in it were derived from real screenshot tools and are
   currently protected by nothing.
2. **xterm.js.** It needs a real window to paint. This is also the known-hard one — see
   *Verifying xterm in a real window* in my memory: a hidden preview pane never paints, so this suite
   has to drive a genuinely visible browser.
3. **One full-stack smoke.** Kestrel + a real SignalR circuit + LiteDB on disk + a pty, wired
   together. Every layer of that is tested in isolation today and the composition is not.

Everything else — component behaviour, rules, wording, which branch rendered — stays in bUnit, and a
spec that could have been a bUnit test should be moved rather than kept.

## The decision that shapes the suite

**How does the app under test get a deterministic agent?** Nothing else in the plan matters until
this is settled, because the terminal and the smoke both need a session that starts.

**Recommended: `WebApplicationFactory<Program>` on a real Kestrel port, with `ConfigureTestServices`
replacing both `IAgentAdapter` registrations with `MockAgentAdapter` from `Act.TestSupport`.**

- It exercises the *true* composition root — `AddActApp`, the hook endpoint binding, the startup
  sequence, the pumps — rather than a copy of `Program.cs` that will drift.
- The mock adapter already lives in `Act.TestSupport` precisely so "both the core tests and the
  Playwright UI tests can drive deterministic sessions" (`repository-structure.md`). This is that
  decision being cashed in.
- No production code changes. `Program.cs` needs `public partial class Program { }` (or the existing
  `InternalsVisibleTo` pattern extended to the new project) and nothing else.
- The one fiddly part: `WebApplicationFactory` normally swaps in an in-memory `TestServer`, which
  Playwright cannot reach over HTTP. The standard workaround is to override `CreateHost` and start a
  second host on Kestrel at a chosen port. It is ~15 lines and well-trodden, but it *is* the piece
  most likely to fight back — build it first and prove it before writing any spec.

**Rejected: a fake CLI on disk, pointed at by `AgentDefaults.Binary`.** Attractive — a real pty
running a real program, no test wiring in the host — but it does not survive contact with the code:
`PtyHost` hands `ExecutableResolver.Resolve(...)` straight to the pty as `App`, and unlike
`CommandHost` it has **no `cmd /c` shim**, so a `.cmd`/`.sh` fake will not spawn on Windows. Pointing
`Binary` at `node.exe` does not help either, since the adapter owns the argument order and the script
path cannot be made the first argument.

**Also rejected: a production switch** (`Act:UseMockAgent` or similar). It fails the repo's own rule
that `appsettings` holds facts about the machine and the vendor while the store holds the user's
choices — a test hook is neither, and it would be a permanent seam in shipped code.

## Hard rules for this suite

Non-negotiable, and the reason they are at the top: on 2026-08-05 a session emptied the live archive
and lost an attachment. A browser suite that writes is exactly the shape that can repeat that.

- **A fresh `ACT_DATA_DIR` per run**, under the temp folder, deleted on teardown. Never the default,
  never `%LOCALAPPDATA%\ACT.Development`.
- **Never port 5210.** Allocate a free port per run; that port is the user's app.
- **Kill what you started.** The fixture owns the host's lifetime and disposes it even when a spec
  throws — a leaked Kestrel holding a temp store is how the next run starts dirty.
- **Startup sweeps and purges delete files.** `AttachmentSweep` runs on every boot, which is another
  reason the store must be the suite's own.
- **Selectors are the app's own classes**, same rule the bUnit suite follows — `div.strip`,
  `button.launch`, `div.dropzone`. No `data-testid`.
- **Never a real agent — decided 2026-08-05.** Every session in this suite goes through
  `MockAgentAdapter`. A spec that would need a real CLI is not written; it is either reshaped to run
  on the mock or dropped. This is the existing "no automated integration tests against real CLIs"
  rule applied to the browser tier, and it is also what makes the suite runnable on a machine with
  neither CLI installed.
- **No sleeping.** `AgentScript.AwaitsKeystroke()` blocks the scripted session until the matching
  input actually arrives, so a round trip through the terminal synchronises itself. Any spec reaching
  for a timer is a spec that has not found the real signal — Playwright's own auto-waiting covers the
  DOM side.

## Phase E0 — the harness

- [ ] `tests/Act.App.E2eTests`, added to `Act.slnx`. `Microsoft.Playwright.Xunit` 1.61.0 (official
      xUnit fixtures) + a project reference to `Act.App` and `Act.TestSupport`
- [ ] `ActAppFixture` — free port, temp data dir, mock adapters, `Usage:Enabled=false`,
      `Telemetry:Enabled=false`, Electron off. Async lifetime; teardown kills the host and deletes
      the directory
- [ ] Prove the Kestrel-hosted `WebApplicationFactory` works and Playwright can load `/` **before**
      writing a single spec. If this fights, stop and reconsider — everything downstream depends on it
- [ ] `Seed` helper: write `act.db` directly through `Act.Infrastructure`'s own store before the host
      starts, so a spec begins from a known board without UI ceremony. Same code the app reads with,
      so the schema cannot drift
- [ ] `dotnet tool install Microsoft.Playwright.CLI` into `.config/dotnet-tools.json` (the placeholder
      already names playwright), plus the `playwright install chromium` step
- [ ] Every test carries `[Trait("Category", "E2E")]`

## Phase E1 — the JS modules

The real justification. Roughly one spec per contract, not per module.

- [ ] **`act-attach` — drop.** Drop files onto the task form (synthesise a `DataTransfer` and dispatch
      `drop`): chips appear, the dropzone highlights while dragging and un-highlights after. Also that
      the document-level `preventDefault` holds — a dropped file must **not** navigate the page away,
      which on Blazor Server discards the circuit and everything typed
- [ ] **`act-attach` — the enter/leave counter.** Dragging across a child element must not flicker the
      highlight; the depth counter is what makes it steady and is invisible to every other tier
- [ ] **`act-attach` — paste, the ambiguous clipboard.** The subtlest rule in the codebase and the one
      most worth pinning: a clipboard carrying *both* an image and text (the Excel/Word case) must
      yield to the text and attach nothing, while an unambiguous file paste attaches. Also that a
      populated `files` wins over `items` rather than being concatenated — a screenshot tool puts the
      same picture on the clipboard twice
- [ ] **`act-attach` — `place`.** Hover a chip: the preview becomes visible, sits below the row when
      there is room and above it when there is not, and stays inside the viewport at the right-hand
      edge. Assert computed position, not pixels
- [ ] **`act-escape` — the three claimants.** Escape leaves the page; Escape with a Radzen dropdown
      open closes the dropdown and stays; Escape with focus inside `.xterm` does nothing, because a
      key typed at an agent is a message to the agent
- [ ] **`act-unsaved`.** A dirty task form arms `beforeunload` and a clean one does not — driven
      through Playwright's `dialog` event on a navigation away
- **`act-presence` — skipped, decided 2026-08-05.** Its whole output is one
  `hasFocus() && visibilityState === 'visible'` fed to `UiPresence`, and the C# half of that gate is
  already covered by four `MainLayoutTests` cases. Driving genuine window focus in headless Chromium
  is unreliable enough that the spec would assert Playwright can dispatch a focus event rather than
  that ACT behaves. Revisit only if the toast gate misbehaves in real use.

## Phase E2 — xterm

Paint, typing and backlog replay are covered by **S5** below, since they are the round trip rather
than a property of the module. What is left here is the two behaviours S5 walks past — both of them
guards that were written because the unguarded version misbehaved, and both invisible everywhere else:

- [ ] **Resize refits once.** `refit` is deliberately guarded against re-applying the same geometry:
      calling it unconditionally from the `ResizeObserver` is a feedback loop, and every resize makes
      the CLI repaint its whole screen. Resize the viewport, assert `OnResize` reports the new
      geometry, and assert it does **not** keep firing
- [ ] **Restart clears before rebinding.** The new session replays its own backlog, and the dead one's
      output left above it would read as one continuous screen when the two share nothing

## Phase E3 — the round trips

### What a round trip is actually for

Not "test the board again" — bUnit does that in 3 ms per case. These exist because five things are
only real once a browser is talking to a running server, and **none of them is exercised anywhere
else in the build**:

1. **The circuit.** Prerendered HTML handed over to an interactive SignalR connection. bUnit has no
   circuit at all; if the handover breaks, every one of the 607 component tests still passes.
2. **The store on disk.** bUnit runs on `FakeCardStore`. Here it is real LiteDB — the `BsonMapper`,
   the schema version, the counter document that mints card numbers.
3. **Routing.** bUnit's `NavigationManager` is a fake that records urls. Here a navigation actually
   re-renders a page and a bookmarked url actually resolves.
4. **Radzen's JS half.** Dropdown panels, dialog masks mounted at `<body>`, the split button's popup
   — bUnit sees the markup Radzen emits, never the behaviour its script adds.
5. **The push path.** A card moving on screen because an *agent event* arrived, with nobody touching
   the browser: adapter → `SessionEventPump` → `BoardState` → `StateHasChanged` → SignalR → DOM.
   This is the essence of ACT and it is untested end to end today.

**What they deliberately do not prove:** the pty. `MockAgentAdapter` hands back a `MockAgentSession`,
so no process is spawned and `PtyHost` is never touched. Real-CLI and real-pty behaviour stays
manual, per each roadmap step's *verify* line. Say so out loud rather than letting a green smoke suite
imply otherwise.

### Six small ones, then a capstone

Small round trips first, each isolating one layer so a failure names its own cause. The long one is
last and exists only to catch what the small ones cannot: state left behind between steps.

- [ ] **S1 — the circuit is alive.** Load `/`; five lanes render; click the density chip; it flips
      *and* the change survives a reload. The ten-second sanity check — if the prerender→interactive
      handover is broken, this is the only test in the build that notices
- [ ] **S2 — a task is created and stays created.** `/card/new` → title, prompt, working directory →
      Save → a strip appears in Preparing carrying a real card number. Reload the page: still there.
      Proves the form, the real store and the number counter. (No app restart — `Act.Infrastructure`
      already owns store round-trip and restart survival; repeating it here buys nothing)
- [ ] **S3 — the launch round trip.** A Ready card → Launch → the strip moves to Executing and shows
      `running`, with no reload. Script: `AgentScript.Start().Activity()`. Proves `SessionLauncher` →
      registry → adapter → event pump → board → circuit → DOM
- [ ] **S4 — the board moves on its own.** The one no other tier can do: with the card Executing and
      **the browser untouched**, the scripted session emits `RequestsPermission` — the strip must
      leave Executing for Your turn, take the amber rail and start blinking. Script:
      `Start().Activity().RequestsPermission("write a file").AwaitsKeystroke()`. If ACT has one
      irreplaceable behaviour, this is it
- [ ] **S5 — the terminal round trip.** Open the card's terminal: xterm paints what the script
      painted (read the buffer, not a screenshot — see the memory note on the hidden preview pane);
      type an answer; the mock session records the keystrokes and the script advances past
      `AwaitsKeystroke`; navigate to the Task tab and back, and the backlog replays rather than the
      screen coming back blank. Script:
      `Start().Paints("Proceed? [y/n] ").AwaitsKeystroke().EndsTurn()`
- [ ] **S6 — sign-off and the way back.** Your turn → *Mark completed* → the card lands in Completed
      and the page returns to the board → archive it from the task page → it appears in `/archive` →
      restore → it is on the board again. Proves the two hand-driven moves across the launch boundary
      plus the archive page's own writes, against the real store

- [ ] **S7 — the capstone, one narrative.** Create → launch → the agent asks → answer it in the
      terminal → the turn ends → sign off → archive. Deliberately duplicates S2–S6; what it adds is
      the transitions *between* them, which is where leftover state hides. **It is also the slowest
      and the most likely to flake, so it is the first thing to cut** if the suite starts costing more
      than it returns

### Two more worth considering, not yet committed

Both are real round trips that nothing else covers, but neither is load-bearing. Decide after S1–S7
are green:

- **The queue starting a card by itself.** Set the schedule to *now*, and with the browser untouched
  watch `QueueRunner` launch it and the strip move Ready → Executing. The unattended path is the one
  users trust most and watch least.
- **A follow-up spawning.** `AgentScript.WritesFollowUps(...)` → a child card appears in Preparing
  while the parent is still running, and archiving the parent asks what to do with it. The dialog
  half is covered by bUnit; what is not is the card appearing on the board with nobody looking.

## Phase E4 — theme and layout (optional)

- [ ] Two or three **computed-style** assertions: the flight-strip rail colour under dark and light,
      and that the board does not scroll horizontally at the 960px floor
- [ ] **No pixel snapshots.** They are flaky across platforms and every legitimate CSS change costs a
      re-baseline — a cost a solo project should not take on

## CI — deferred, run by hand for now

**Decided 2026-08-05: this suite is launched manually.** No CI job, no nightly. What CI would take is
recorded in *Deferred — Playwright in CI* in `ACT-roadmap.md`; revisit it when the suite has proven
itself stable enough to gate anything.

One thing is still required *now*, because it is what makes manual-only actually hold:

- [ ] `scripts/build.ps1` runs `dotnet test $solution`, which would sweep this suite into every local
      and CI run the moment the project exists. Default test step becomes `--filter "Category!=E2E"`,
      with an `-E2E` switch that runs only that category. Without it "manual" lasts exactly as long as
      it takes someone to run the build script

## Cost, honestly

Around eighteen specs and one fixture, run by hand. The fixture is the real work and the one thing
that can go badly; the specs themselves are small.

**If the Kestrel-hosted factory turns out to be a fight**, the fallback is *not* to reach for a real
agent — that is ruled out above. It is to keep the specs that need no session at all and drop the
rest: E1 minus nothing, plus S1 and S2, all of which run against the app launched as an ordinary
process with a sandboxed data directory (the command already in `CLAUDE.md`) and no mock adapter
anywhere. That keeps `act-attach` — 214 lines of measured clipboard and drag behaviour protected by
nothing today, and by some distance the largest uncovered risk in the codebase. What it costs is
S3–S7 and the xterm phase, which are the parts that need a session to exist.

Order of work, if it goes well: E0 → S1 → E1 → S2–S6 → E2 → S7. E1 comes early on purpose — it is the
phase that justifies the tier, so it should land before the effort is sunk into anything else.

## Log

- **2026-08-05** — plan written. Nothing implemented yet.
- **2026-08-05** — decisions taken: CI deferred, suite runs by hand (roadmap and spec updated);
  `act-presence` skipped; **never a real agent** — every session goes through `MockAgentAdapter`, and
  a spec that cannot be reshaped onto the mock is dropped rather than written. Phase E3 expanded from
  one smoke into six small round trips plus a capstone.
