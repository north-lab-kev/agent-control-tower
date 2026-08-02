# ACT — Repository structure

Standard modern-.NET layout (`src/` + `tests/` split, central build/package
props, `global.json` SDK pin, `.github/`) mapped onto ACT's ports-and-adapters
design. **The project boundaries are the architecture:** `Act.Core` depends on
nothing infrastructural; adapters, infrastructure, and UI all point *inward* to
its `Abstractions/` interfaces.

```
agent-control-tower/                 # repo root (slug); brand "ACT" lives in README
│
├─ .github/
│  ├─ workflows/
│  │  ├─ ci.yml                      # build + fast test suite (Linux on push; Windows occasional while private)
│  │  └─ release.yml                 # tag → release build + desktop artifact
│  ├─ ISSUE_TEMPLATE/
│  ├─ PULL_REQUEST_TEMPLATE.md
│  └─ dependabot.yml
│
├─ docs/                            # design docs
│  ├─ ACT-overview.md               # the spec
│  ├─ ACT-roadmap.md                # the build plan
│  ├─ repository-structure.md       # this file
│  └─ ui-preview.html               # UI mockup
│
├─ src/
│  ├─ Act.Core/                     # DOMAIN + APPLICATION — no infra/UI/agent deps
│  │  ├─ Model/                     #   Card, Column, Badge, Transition, Lineage…
│  │  ├─ Events/                    #   normalized event types (the adapter vocabulary)
│  │  ├─ Agents/                    #   PtyAgentSession/Terminal, launch-config resolution,
│  │  │                            #     agent process environment, TranscriptTail (offset +
│  │  │                            #     running snapshot) — all shared by both adapters
│  │  ├─ Rules/                     #   the rules engine, manual-move validity, sign-off and
│  │  │                            #     reopen validity,
│  │  │                            #     Your-turn ordering, which cards are resumable after their
│  │  │                            #     process died, how long a running card has been quiet,
│  │  │                            #     which Completed cards retention is due to archive,
│  │  │                            #     which card is already working in a folder a launch
│  │  │                            #     wants (pure logic)
│  │  ├─ Scheduling/                #   queue runner, schedule, backpressure
│  │  ├─ Resources/                 #   CoreStrings (+ .fr) — localised text the CORE writes,
│  │  │                            #     e.g. the autoGit sentence appended to a prompt
│  │  └─ Abstractions/              #   INTERFACES: IAgentAdapter, IAgentSession,
│  │                               #     IAgentTerminal, IPtyHost, IAgentEventSink,
│  │                               #     ITranscriptReader, ITranscriptNormalizer,
│  │                               #     IUsageProbe, IUsageDialect, ITextFileReader,
│  │                               #     IAgentCapabilityCatalog, ICardStore, INotifier, IClock…
│  ├─ Act.Agents.ClaudeCode/        # Claude Code adapter (pty command line, hook + mcp settings,
│  │                               #   claude:// handoff, mappings)
│  ├─ Act.Agents.Codex/             # Codex adapter
│  ├─ Act.Infrastructure/           # LiteDB store, localhost hook + mcp host, transcript tailer,
│  │  │                            #   process supervision, Serilog wiring
│  │  ├─ Storage/                   #   act.db open + BsonMapper, schema version, card/settings stores
│  │  ├─ FileSystem/                #   IWorkingDirectories: ~ expansion, path validation, browsing
│  │  ├─ Power/                     #   ISleepInhibitor: SetThreadExecutionState (Windows),
│  │  │                            #     caffeinate (macOS), systemd-inhibit (Linux)
│  │  ├─ Terminal/                  #   IPtyHost over Porta.Pty: spawn, incremental UTF-8 decode,
│  │  │                            #     batched flush, capped scrollback, resize, kill
│  │  ├─ Hooks/                     #   hook endpoint + per-session token + kept-port store and
│  │  │                            #     binder + guard rule + generated agent config files.
│  │  │                            #     No ASP.NET: the routing/middleware half is Act.App/Hooks/
│  │  ├─ Transcripts/               #   ITranscriptReader over a JSONL the agent still holds open:
│  │  │                            #     read from an offset, leave a partial line, restart on
│  │  │                            #     truncation
│  │  └─ Usage/                     #   HttpUsageProbe: credential file → bearer → GET → dialect.
│  │                               #     UsageOptions binds the "Usage" appsettings section
│  │                               #     (poll interval, per-agent path/endpoint overrides)
│  ├─ Act.App/                      # Blazor Server UI + Electron desktop host (ElectronNET.Core)
│  │  ├─ Components/
│  │  │  ├─ Pages/                  #     EVERY @page component and nothing else:
│  │  │  │                          #       BoardView "/", TaskView, SessionView,
│  │  │  │                          #       ArchiveView, SettingsView, Error, NotFound
│  │  │  ├─ Board/                  #     non-routable board parts (FlightStrip)
│  │  │  ├─ Shared/                 #     CardTabs (Task|Terminal|Timeline), FolderPicker,
│  │  │  │                         #       EscapeKey (renders nothing; binds Escape for a page)
│  │  │  └─ Layout/                 #     MainLayout, top bar, theme stylesheets
│  │  ├─ Desktop/                   #   DesktopShell: the Electron window + tray icon and the
│  │  │                            #     close/exit rules (Electron-only; registered when enabled)
│  │  │                            #     + IDesktopBridge / INotifier and their two
│  │  │                            #     implementations each — the only shell surface a page may
│  │  │                            #     touch. With Program.cs and the DI extensions, the ONLY
│  │  │                            #     place allowed to name ElectronNET (a test enforces it)
│  │  ├─ Cards/                     #   BoardState — owns every card incl. archived ones, and
│  │  │                            #     decides what may see which; RetentionPump (the hourly
│  │  │                            #     auto-archive sweep); task form model; capability
│  │  │                            #     catalog built from the registered adapters
│  │  ├─ Sessions/                  #   SessionRegistry (the live sessions), SessionLauncher
│  │  │                            #     (launch + restore), SessionRestorer (startup re-attach),
│  │  │                            #     CardCompleter / CardReopener (the two hand-driven moves
│  │  │                            #     past the launch boundary),
│  │  │                            #     SessionEventPump (drains events onto the board),
│  │  │                            #     TranscriptPump (one polling tail per live session)
│  │  ├─ Notifications/             #   NotificationDispatcher (setting + focus gate + one ping
│  │  │                            #     per state, and the wording), UiPresence (who is looking
│  │  │                            #     at what), DeepLinkRouter (clicked toast → that card)
│  │  ├─ Usage/                     #   UsageState (latest result per agent, reading or named
│  │  │                            #     unavailability) + UsagePump (one poll loop per probe,
│  │  │                            #     backing off on a failure that cost a request);
│  │  │                            #     UsageIndicator renders both in the top bar
│  │  ├─ Settings/                  #   user-settings service + culture
│  │  ├─ Resources/                 #   .resx strings (en / fr)
│  │  ├─ wwwroot/                   #   CSS (flight-strip look), assets
│  │  │  ├─ lib/xterm/              #     xterm.js + fit addon (vendored UMD) + VENDOR.md
│  │  │  │                         #       pinning version + SHA-256 of each file
│  │  │  └─ js/                     #     interop modules: act-terminal (attach / write / resize /
│  │  │                            #       dispose), act-unsaved (the window-close guard),
│  │  │                            #       act-escape (Escape leaves the page it is bound on),
│  │  │                            #       act-presence (focus + visibility, for the toast gate)
│  │  ├─ Properties/                #   launchSettings + electron-builder.json (packaging)
│  │  ├─ ServiceCollectionExtensions.cs
│  │  │                            #   AddActApp: every Act.App registration (agents, board,
│  │  │                            #     sessions) + AddActDesktopShell for the Electron branch
│  │  └─ Program.cs                 #   startup pipeline; Electron wired only when enabled
│
├─ tests/
│  ├─ Act.Core.Tests/               # xUnit — rules engine & scheduler (hard, pure)
│  ├─ Act.Infrastructure.Tests/     # store round-trip, restart survival, schema versioning
│  ├─ Act.Agents.Tests/             # contract tests: one suite every adapter must pass
│  ├─ Act.App.UiTests/              # Playwright (browser-mode Blazor, mock-adapter driven)
│  └─ Act.TestSupport/              # the MOCK adapter + fixtures/builders (shared)
│
├─ scripts/
│  ├─ build.ps1                     # repeatable build + test (CI hook); cross-platform pwsh
│  └─ package-desktop.ps1           # Electron.NET desktop packaging
│
├─ Act.slnx                         # XML solution format
├─ Directory.Build.props            # shared: <Nullable>enable</Nullable>, analyzers, warnings-as-errors on core
├─ Directory.Packages.props         # central package version management
├─ global.json                      # pin the .NET 10 SDK
├─ .editorconfig                    # style rules (enforced by dotnet format)
├─ .config/dotnet-tools.json        # local tools: dotnet-format, coverlet, playwright
├─ .gitignore                       # dotnet (bin/obj) + node (node_modules, Electron output)
├─ README.md                        # "# ACT — Agent Control Tower" + description
├─ LICENSE.md                       # FSL (added manually — not in GitHub's picker)
├─ THIRD-PARTY-NOTICES.md           # permissive-dependency attribution
└─ CONTRIBUTING.md                  # + CLA/DCO note (protects future dual-licensing)
```

## Key decisions

- **Dependency direction is the whole point.** `Act.Core` references nothing
  below it; adapters, infrastructure, and UI depend on Core's `Abstractions/`
  interfaces. This is what makes the rules engine unit-testable in isolation and
  lets the mock adapter stand in for a real CLI.
- **The mock adapter lives in `Act.TestSupport`** (not buried in one test
  project) so both the core tests and the Playwright UI tests can drive
  deterministic sessions through it. It ships with `AgentScript` (a lifecycle as one
  fluent line) and `TestClock`, and is constructed for a chosen `AgentType` so the
  same script can be replayed as either agent.
- **The core has resources of its own, and text sent to an agent is localised.**
  `Act.Core/Resources/CoreStrings.resx` (+ `.fr`) is generated by the same
  `Microsoft.CodeAnalysis.ResxSourceGenerator` the app uses, so the core does not have to
  reach up to `Act.App.Resources` — which it cannot, and should not. The rule is that
  **anything ACT writes is localised, including what it writes to an agent**: the agent is
  addressed in the language the user runs ACT in. `AppCulture` sets
  `DefaultThreadCurrentUICulture` process-wide, so a core-side lookup resolves correctly
  from any thread, and `Act.Core.resources.dll` ships beside `Act.App.resources.dll`.
- **The agent↔ACT contract is Core's, not an adapter's** — and as of 2026-07-30 it is
  a single MCP tool (`create_followup`), not a file convention. `ActContract` and
  `AgentPreamble` are gone with the status file and the injected preamble; the tool's
  schema and handler belong to Core, and an adapter only decides how the MCP server is
  *declared* to its CLI (`--mcp-config` vs. a `--profile` table). Same principle, one
  fewer moving part: see the spec's *Agent ↔ ACT contract*.
- **`Components/Pages/` holds every routable component, and nothing else.** The rule is
  simply *a component with `@page` lives in `Pages/`* — so the folder listing is the
  route list, and there is one place to look. Non-routable components stay with their
  feature (`Board/FlightStrip`). The routes:

  | Route | Page |
  |---|---|
  | `/` | `BoardView` |
  | `/card/new` · `/card/{id}/edit` | `TaskView` |
  | `/card/{id}/terminal` | `SessionView` |
  | `/card/{id}/timeline` | `TimelineView` |
  | `/archive` | `ArchiveView` |
  | `/settings` | `SettingsView` |
  | `/Error` · `/not-found` | `Error` · `NotFound` |

  `grep -rn "^@page" src/Act.App` regenerates that table; keep it in step with the code.
- **A card's faces are tabs over routes, not a toggle.** `CardTabs` sits on its own row under
  the back-to-board bar — which is what gives the title the full width beside it — and each tab
  is a `NavLink` to that face's url rather than a panel, so `active` state needs no C# and a
  face is directly linkable. `Timeline` renders the card's stored transitions with the
  mockup's rail (a dot per event coloured by the badge that reported it, a hairline joining
  them, mono times held to the right). Note the CSS needs `::deep` to reach a `NavLink`'s
  anchor: scoped styles stop at a child component's boundary, and without it the tabs arrive as
  default blue links.
- **Pages are destinations; dialogs are forks.** The new-task modal, the edit modal and the
  settings dialog all became routes, because each was somewhere you *go*. What stayed modal
  is the opposite kind of thing: a prompt that interrupts an action already under way and
  would be meaningless as a URL — archiving a card with follow-ups, and emptying the
  archive. Those two are the whole list: step 11's completion/git prompt was cut with the
  spawned git task (2026-08-01), so sign-off and reopen are plain drags. `RadzenComponents`
  and the `-webkit-app-region: no-drag` rule are kept for the two that remain.
- **The hook endpoint is two listeners on one web host, not a second server.** The UI keeps
  the app's address; `/hooks/claude` and `/hooks/codex` answer on a loopback port ACT
  allocates once and remembers. `HookPortGuard` is what makes the two ports mean different
  things — hooks 404 off the hook port, the UI 404 on it. `Act.Core` holds only the ports
  (`IHookEndpoint`, `IHookNormalizer`, `IAgentEventSink`, `IAgentConfigFiles`) plus the wire
  constants both sides must agree on (`Agents/HookTransport`); each **normalizer lives with
  its adapter**, because a payload dialect is a fact about that CLI exactly like its command
  line. `SessionEventSink` in `Act.App` is the one piece that knows how to turn the task id a
  token proves into the live session an event belongs to.
  - **The endpoint is split by capability, not by feature.** `Act.Infrastructure/Hooks/` keeps
    everything web-free and unit-testable — token registry, port allocator and store, the guard
    *rule*, the normalizing dispatcher, the config-file writer — and `Act.App/Hooks/` holds the
    ~100 lines that genuinely need ASP.NET: the address, the middleware, the two routes. It was
    briefly all in infrastructure behind a `Microsoft.AspNetCore.App` framework reference, and
    that reference was the problem: a capability grant to a project of passive port adapters,
    bought for one file, that would have let anything there reach for `HttpContext`. Registration
    stays with the code (`HookRegistration`, called by `AddActInfrastructure`), and the app is
    left to sequence the three calls only it can — bind, guard, map.
- **The transcript tail is the hook pipeline's mirror image, and lands the same way.** Same
  three-way split: the port and the stateful tail in `Act.Core` (`ITranscriptReader`,
  `ITranscriptNormalizer`, `Agents/TranscriptTail`), the file handling in
  `Act.Infrastructure/Transcripts/`, and the **line dialect with its adapter** — a JSONL shape
  is a fact about that CLI exactly like its payload shape. `Act.App/Sessions/TranscriptPump`
  owns the loop, because a poll per live session is session lifetime and that lives with the
  registry. The file system stays behind a port for the same reason `IAgentConfigFiles` does:
  the adapters must not reach sideways into infrastructure, and the fold has to be testable
  with a string instead of a file.
- **The usage probe is the hook and transcript split a third time, and the paths it reads
  are discovered, never hardcoded.** Same three-way shape: the ports in `Act.Core`
  (`IUsageProbe`, `IUsageDialect`, `ITextFileReader`), the transport in
  `Act.Infrastructure/Usage/HttpUsageProbe`, and the **dialect with its adapter** — where a
  CLI keeps its credentials, which endpoint answers for it, how to lift a token out of the
  one and windows out of the other are facts about that CLI exactly like its hook payload.
  Each dialect resolves its own default path from the environment (`CLAUDE_CONFIG_DIR` /
  `CODEX_HOME`, else the CLI's folder under the user profile), because ACT ships to machines
  whose home directory it cannot know. `appsettings.json` → `Usage:Agents:<agent>` can override
  the path and the endpoint per agent, but **ships absent rather than blank**: an empty string
  configures nothing, and a file full of empty keys reads as "fill these in" for values that are
  supposed to be found. The measured endpoints, response shapes and traps are in
  `docs/agent-usage-findings.md`.
  - **`appsettings` holds facts about the machine and the vendor; the store holds the user's
    choices.** That is the whole line, and it decides where a knob goes. A credential path
    and an endpoint are there for when discovery is wrong on *this* machine or a vendor moves
    a URL — nobody chooses them, and a user who needs one is unblocking a broken install, not
    setting a preference. `Usage:Enabled` is the same kind of thing: a deployment with no
    outbound network turns the feature off, and it is not a switch the UI should offer.
    **Whether to watch an agent at all is a preference, so it lives in the store** — the
    *Agents* section's enabled switch, read by `UsagePump` on every pass. A per-agent
    `Usage:Agents:<agent>:Enabled` existed alongside it briefly and was removed: two flags
    spelled the same way, one of them invisible, deciding the same thing.
  - **A window is named by the length the server declares, not by a fixed caption pair.**
    A paid Codex plan reports 5-hour plus weekly; a free one reports a single 30-day window
    and no secondary. Hardcoding two bars would mislabel a real account, so
    `UsageWindow.Classify` maps a duration onto `Session`/`Weekly`/`Monthly` and both dialects
    feed it.
  - **A reading held past its own reset reports zero, not the number it was given.** ACT
    polls, so nothing ran in between — the stale percentage describes a window that no longer
    exists. It renders subdued, because it is inferred from this machine only.
  - **The probe returns a named outcome, never a bare null.** `UsageProbeResult` carries a
    `UsageAvailability` so the top bar can say *why* a quota is missing — not signed in, token
    expired, token refused, unreachable, unreadable — and `UsageBackoff` (`Act.Core/Rules`)
    decides how long to wait before asking again, doubling only for the failures that actually
    reached the network.
- **Anything stored is a code; wording is resolved at render time.** A `Transition` keeps a
  `TransitionReason` plus a verbatim `Note` (an exit code, a CLI error, the adjustments a
  launch made) — never a sentence. Two reasons: `Act.Core` has no resources and must not
  acquire any, and a *stored* translation would freeze the language it was written in, so a
  card moved before the user switched to French would keep explaining itself in English.
  `Act.App/Resources/TransitionText` renders one, looking the resource up **by the enum name**
  (`Transition_TurnEnded`) rather than through a switch that could drift; a test asserts
  every reason resolves in every shipped language, and that the French is actually different
  from the English.
  - **A retired reason must not make old cards unreadable.** The vocabulary changes as the
    rules do, and LiteDB's enum deserializer throws on a name the build no longer has — so
    `ActBsonMapper` maps `TransitionReason` itself and reads an unknown name back as **null**,
    which `TransitionText` already renders as the transition's verbatim note. Retiring a
    reason is therefore a code change, not a migration.
- **The pseudo-terminal is a Core port, not adapter code.** Spawning a process under
  a pty and pumping its bytes is agent-agnostic plumbing; only the *command line* is
  agent-shaped. So `IPtyHost` lives in `Act.Core/Abstractions` and its `Porta.Pty`
  implementation in `Act.Infrastructure/Terminal/`, injected into each adapter. The
  alternative — adapters referencing `Act.Infrastructure` — would be a sideways
  dependency between two projects that are both supposed to point *inward*, and it
  would drag `Porta.Pty` into every adapter. It also keeps the contract suite
  process-free: a fake `IPtyHost` is all a test needs.
- **The contract suite covers only the process-free surface.** `AgentAdapterContract`
  in `Act.Agents.Tests` asserts identity, capabilities and launch-config resolution —
  never a live session — because ACT deliberately runs no real CLI in automated
  tests. Live behavior is verified by hand per roadmap step.
- **`INotifier` is an `Act.App/Desktop` port, not a Core one** (decided 2026-08-01,
  when step 13 built it). Nothing in Core calls it: the decision half is a pure rule
  (`Act.Core/Rules/NotificationTrigger`, "is this card wanting something") and the
  App half owns the wording, the settings gate and the transport. So it sits beside
  `IDesktopBridge` under the same test — an interface earns a place in
  `Act.Core/Abstractions` only when Core-side code triggers it, as `ISleepInhibitor`
  does. `ElectronNotifier` and a no-op `BrowserNotifier` are the two
  implementations, registered the same way the bridge is.
- **`Act.App/Notifications/` is the policy, `Desktop/` is the transport.** The
  dispatcher decides *whether* (the setting, the focus gate, one-state-one-ping) and
  composes the text; the notifier only shows what it is handed. `UiPresence` is how
  the server knows whether anyone is looking — `MainLayout` reports focus and route
  per circuit — and `DeepLinkRouter` is the way back, turning a clicked toast into a
  navigation on the live circuit instead of a page reload.
- **`Act.Desktop` is not a project, decided 2026-08-01.** The folder existed as a
  placeholder for a shell-specific concern that never emerged; notifications were
  the first real candidate and they did not change the answer. Splitting would only
  isolate anything if `Act.App` became a *library* and the desktop host became the
  exe — which moves every static asset to `_content/Act.App/…` (the xterm bundle,
  `act-terminal.js`, the theme css, the favicon) and leaves two hosts to keep in
  sync, for ~350 lines of shell code. The cheap half of the benefit is taken
  instead: `ElectronBoundaryTests` fails the build if anything outside `Program.cs`,
  `ServiceCollectionExtensions.cs` and `Desktop/` so much as names ElectronNET.
  Revisit if the shell grows auto-update, native menus and protocol handlers, or if
  a second front-end (remote access) forces the library split anyway.
- **No page references ElectronNET.** `Desktop/IDesktopBridge` carries the only two
  things a view needs from the shell — `IsDesktop`, and `OpenExternalAsync` for a
  `claude://` handoff — with `ElectronDesktopBridge` and `BrowserDesktopBridge`
  behind it. `AddActApp` registers the browser one; `AddActDesktopShell` (the
  Electron-only branch) `Replace`s it, so the composition root is the single place
  that knows which mode ACT is in. The interface stays in `Act.App` rather than
  `Act.Core/Abstractions` because nothing in Core calls it — unlike `INotifier` and
  `ISleepInhibitor`, which Core-side code triggers. `DesktopShell` is deliberately
  *not* behind it: window, tray and native dialogs are inherently Electron, and one
  quarantined class is cheaper than an abstraction with a single implementation.
- **Never ask `HybridSupport.IsElectronActive` during startup.** It reads
  `ElectronNetRuntime.RuntimeController != null`, and for an ASP.NET host that
  controller is only assigned in `RuntimeControllerAspNetBase`'s constructor — i.e.
  when DI resolves it, well after `AddElectron`. Before then it answers `false` in
  *every* mode, packaged or not; that is why `Program.cs` sniffs the
  `/electronPort` argument itself to decide whether to run as a desktop shell. Views
  are free of the timing question because `IDesktopBridge.IsDesktop` is a constant
  per implementation, fixed by the registration.
- **The store owns its own conventions.** `Storage/` keeps one entry point
  (`ActDatabase.Open`) that configures the `BsonMapper` and applies the schema —
  a **version document plus an ordered migration list**, so `CurrentVersion` is
  derived from that list and a store written by a newer build is rejected instead
  of silently mangled. Collection names live in one place (`ActCollections`), and
  the friendly card `number` comes from a counter document that seeds itself
  above any card already stored.
- **No sample data.** `Act.App/Seeding/` existed so the board had something to render
  before tasks could be created, along with a compile-time gate to keep it out of Release.
  Creating a real task is now a page and a Save, so the demo cards, the `#if DEBUG` call
  sites, the csproj `<Compile Remove>` and the `Act:SeedSampleCards` switch are all gone.
  **Deleting the seeder does not delete already-seeded rows** — an existing `act.db` keeps
  whatever it was given; clear it by deleting the file (or the cards) if you want an empty
  board.
- **Node artifacts must be git-ignored** — Electron.NET pulls in `node_modules`
  and a build-output folder; easy to accidentally commit (bloat + secrets risk
  on go-public).
- **Solo-project judgment calls:** domain and application are merged into
  `Act.Core` (not split); adapters are self-contained with no shared
  `Act.Agents.Common` until real shared plumbing emerges (DRY with judgment).

## Notes

- **ACT writes nothing into a task's working directory.** The `.act/` directories
  (`status/`, `followups/`) were dropped on 2026-07-30 — status with auto-completion,
  follow-ups in favour of the MCP tool. Generated launch config (hook settings, Codex
  profile, MCP config) lives in ACT's own data directory instead.
- The **`prototype` branch** (commit `bb7633d`) is reference material for the PTY
  work, not something to merge — it also carries a throwaway `Act.App/Prototype/`
  page and its `Program.cs` wiring. Read individual files with
  `git show prototype:<path>`; `PtySession.cs` and `wwwroot/js/prototype-pty.js` are
  the two worth lifting, and `PtyStateProbe.cs` is the one to leave behind (it exists
  to prove screen-scraping doesn't work).
- `LICENSE.md` is added by hand (copy FSL text from fsl.software); GitHub's
  license picker only lists OSI-approved licenses, so choose "No license" there.
