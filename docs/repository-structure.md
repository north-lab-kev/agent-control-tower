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
│  │  │                            #     agent process environment (shared by both adapters)
│  │  ├─ Rules/                     #   the rules engine, manual-move validity, sign-off validity,
│  │  │                            #     Your-turn ordering, which cards are resumable after their
│  │  │                            #     process died (pure logic)
│  │  ├─ Scheduling/                #   queue runner, schedule, backpressure
│  │  └─ Abstractions/              #   INTERFACES: IAgentAdapter, IAgentSession,
│  │                               #     IAgentTerminal, IPtyHost, IIngestionSource,
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
│  │  └─ Hooks/                     #   hook endpoint + per-session token + kept-port store and
│  │                               #     binder + guard rule + generated agent config files.
│  │                               #     No ASP.NET: the routing/middleware half is Act.App/Hooks/
│  ├─ Act.App/                      # Blazor Server UI + Electron desktop host (ElectronNET.Core)
│  │  ├─ Components/
│  │  │  ├─ Pages/                  #     EVERY @page component and nothing else:
│  │  │  │                          #       BoardView "/", TaskView, SessionView,
│  │  │  │                          #       ArchiveView, SettingsView, Error, NotFound
│  │  │  ├─ Board/                  #     non-routable board parts (FlightStrip)
│  │  │  ├─ Shared/                 #     CardTabs (Task|Terminal|Timeline) + FolderPicker
│  │  │  └─ Layout/                 #     MainLayout, top bar, theme stylesheets
│  │  ├─ Desktop/                   #   DesktopShell: the Electron window + tray icon and the
│  │  │                            #     close/exit rules (Electron-only; registered when enabled)
│  │  │                            #     + IDesktopBridge and its two implementations — the only
│  │  │                            #     shell surface a page may touch
│  │  ├─ Cards/                     #   BoardState — owns every card incl. archived ones, and
│  │  │                            #     decides what may see which; task form model; capability
│  │  │                            #     catalog built from the registered adapters
│  │  ├─ Sessions/                  #   SessionRegistry (the live sessions), SessionLauncher
│  │  │                            #     (launch + restore), SessionRestorer (startup re-attach),
│  │  │                            #     SessionEventPump (drains events onto the board)
│  │  ├─ Settings/                  #   user-settings service + culture
│  │  ├─ Resources/                 #   .resx strings (en / fr)
│  │  ├─ wwwroot/                   #   CSS (flight-strip look), assets
│  │  │  ├─ lib/xterm/              #     xterm.js + fit addon (vendored UMD) + VENDOR.md
│  │  │  │                         #       pinning version + SHA-256 of each file
│  │  │  └─ js/act-terminal.js      #     attach / write / resize / dispose interop module
│  │  ├─ Properties/                #   launchSettings + electron-builder.json (packaging)
│  │  ├─ ServiceCollectionExtensions.cs
│  │  │                            #   AddActApp: every Act.App registration (agents, board,
│  │  │                            #     sessions) + AddActDesktopShell for the Electron branch
│  │  └─ Program.cs                 #   startup pipeline; Electron wired only when enabled
│  └─ Act.Desktop/                  # unused under Option B (Electron lives in Act.App); pending removal
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
  archive. Step 11's completion/git prompt is the same shape. `RadzenComponents`
  and the `-webkit-app-region: no-drag` rule are kept for it.
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
- **Anything stored is a code; wording is resolved at render time.** A `Transition` keeps a
  `TransitionReason` plus a verbatim `Note` (an exit code, a CLI error, the adjustments a
  launch made) — never a sentence. Two reasons: `Act.Core` has no resources and must not
  acquire any, and a *stored* translation would freeze the language it was written in, so a
  card moved before the user switched to French would keep explaining itself in English.
  `Act.App/Resources/TransitionText` renders one, looking the resource up **by the enum name**
  (`Transition_TurnNeedsInput`) rather than through a switch that could drift; a test asserts
  every reason resolves in every shipped language, and that the French is actually different
  from the English.
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
- **`INotifier` is a port.** Native OS notifications come from Electron, which
  under Option B lives in `Act.App` — so the native implementation lives there
  too (gated on Electron being active), with a no-op/web fallback for the plain
  `dotnet run` browser mode. *(Originally slated for `Act.Desktop`; moved when
  Electron wiring was consolidated into `Act.App`.)*
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
