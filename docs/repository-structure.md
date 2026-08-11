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
│  │  └─ release.yml                 # manual (Release Version input): gate (Linux) → package-windows + package-linux → tag + release (Linux)
│  ├─ ISSUE_TEMPLATE/
│  ├─ PULL_REQUEST_TEMPLATE.md
│  └─ dependabot.yml
│
├─ docs/                            # design docs
│  ├─ overview.md                   # the spec's index: reading order + status
│  ├─ spec/                         # the spec itself, one file per area
│  ├─ user-guide.md                 # how to use ACT, for end users
│  ├─ development.md                # build, run, test, CI, engineering standards
│  ├─ repository-structure.md       # this file
│  ├─ design-notes.md               # how the non-obvious decisions were reached: measurements,
│  │                                #   rejected shapes, deleted code. The code cites it rather
│  │                                #   than carrying the history inline
│  └─ findings/                     # measured investigations, cited from the code
│     ├─ agent-usage.md             # the two usage endpoints and their traps
│     ├─ agent-title.md             # the one-shot query: measured flags per CLI, the stdin and
│     │                             #   stream-splitting traps, why --bare breaks auth
│     └─ codex-hooks.md             # Codex hook discovery, TOML shape, the quoting bug
│
├─ src/
│  ├─ Act.Core/                     # DOMAIN + APPLICATION — no infra/UI/agent deps
│  │  ├─ Model/                     #   Card, Column, Badge, Transition, Lineage…
│  │  ├─ Events/                    #   normalized event types (the adapter vocabulary)
│  │  ├─ Agents/                    #   PtyAgentSession/Terminal, launch-config resolution,
│  │  │                            #     agent process environment, TranscriptTail (offset +
│  │  │                            #     running snapshot), TaskTitleQuery (the title ACT asks an
│  │  │                            #     agent for, and the shaping of what comes back) — all
│  │  │                            #     shared by both adapters
│  │  ├─ Rules/                     #   the rules engine, manual-move validity, sign-off and
│  │  │                            #     reopen validity,
│  │  │                            #     where a card sits in its column (CardOrder — the board's
│  │  │                            #     order and the queue's are one rule),
│  │  │                            #     which cards are resumable after their
│  │  │                            #     process died, how long a running card has been quiet,
│  │  │                            #     which Completed cards retention is due to archive,
│  │  │                            #     which card is already working in a folder a launch
│  │  │                            #     wants, which cards a filter query matches (pure logic)
│  │  ├─ Scheduling/                #   the queue decision as pure logic: LaunchQueue (one pass →
│  │  │                            #     ordered launch list + a hold per Ready card), ScheduleArming,
│  │  │                            #     ConcurrencySlots, DependencyGate, UsageBackpressure,
│  │  │                            #     SleepPolicy, QueuePolicy/LaunchHold
│  │  ├─ Telemetry/                 #   what the cloud metrics may say, as pure logic: TelemetryEvents
│  │  │                            #     (the ONLY place a payload is built — three factories taking
│  │  │                            #     typed values, never a property bag; the app and its settings,
│  │  │                            #     never how a card went), TelemetryProperties
│  │  │                            #     (the declared keys), TelemetryPayload (drops any undeclared
│  │  │                            #     name/key or unbounded value), TelemetryFault (unwraps to the
│  │  │                            #     exception that actually failed; frames as Type.Method
│  │  │                            #     (File.cs:42) — ACT's own source, never the message)
│  │  ├─ Resources/                 #   CoreStrings (+ .fr) — localised text the CORE writes,
│  │  │                            #     e.g. the autoGit sentence appended to a prompt
│  │  └─ Abstractions/              #   INTERFACES: IAgentAdapter, IAgentSession,
│  │                               #     IAgentTerminal, IPtyHost, ICommandHost, IAgentEventSink,
│  │                               #     ITranscriptReader, ITranscriptNormalizer,
│  │                               #     IUsageProbe, IUsageDialect, ITextFileReader,
│  │                               #     IAgentCapabilityCatalog, ICardStore, INotifier, IClock,
│  │                               #     ITelemetrySink…
│  ├─ Act.Agents.ClaudeCode/        # Claude Code adapter (pty command line, hook + mcp settings,
│  │                               #   claude:// handoff, mappings)
│  ├─ Act.Agents.Codex/             # Codex adapter
│  ├─ Act.Infrastructure/           # LiteDB store, localhost hook + mcp host, transcript tailer,
│  │  │                            #   process supervision, Serilog wiring
│  │  ├─ Logging/                   #   THE ONLY PLACE THAT NAMES SERILOG (a test enforces it):
│  │  │                            #     ActLogging (the provider + rolling file sink),
│  │  │                            #     ActLogFormatter (the line shape), ActLogDirectory /
│  │  │                            #     ActLogLocation (where the files are, Serilog-free so a
│  │  │                            #     page can ask), ActLogScope (the Task/Session correlation
│  │  │                            #     scope every call site shares — MEL only)
│  │  ├─ Storage/                   #   act.db open + BsonMapper, schema version, card/settings stores
│  │  ├─ FileSystem/                #   IWorkingDirectories: ~ expansion, path validation, browsing.
│  │  │                            #     Also IAttachmentStore + AttachmentStore — a task's files
│  │  │                            #     under attachments/<cardId>/, with the name sanitising,
│  │  │                            #     collision suffixing and prune/copy/clear the lifecycle needs.
│  │  │                            #     Also IFileWatcher + FileWatcher — a change signal on one
│  │  │                            #     file, how UsagePump ends a backoff wait early
│  │  ├─ Power/                     #   ISleepInhibitor: SetThreadExecutionState (Windows),
│  │  │                            #     caffeinate (macOS), systemd-inhibit (Linux)
│  │  ├─ Terminal/                  #   IPtyHost over Porta.Pty: spawn, incremental UTF-8 decode,
│  │  │                            #     batched flush, capped scrollback, resize, kill.
│  │  │                            #     Also ICommandHost — the same PATH walk with no terminal:
│  │  │                            #     one short run, both streams captured, stdin closed, a
│  │  │                            #     deadline, and a cmd /c shim for a .cmd install
│  │  ├─ Hooks/                     #   hook endpoint + per-session token + kept-port store and
│  │  │                            #     binder + guard rule + generated agent config files.
│  │  │                            #     No ASP.NET: the routing/middleware half is Act.App/Hooks/
│  │  ├─ Transcripts/               #   ITranscriptReader over a JSONL the agent still holds open:
│  │  │                            #     read from an offset, leave a partial line, restart on
│  │  │                            #     truncation
│  │  ├─ Usage/                     #   HttpUsageProbe: credential file → bearer → GET → dialect.
│  │  │                            #     UsageOptions binds the "Usage" appsettings section
│  │  │                            #     (poll interval, per-agent path/endpoint overrides)
│  │  └─ Telemetry/                 #   THE ONLY PLACE THAT NAMES POSTHOG (a test enforces it):
│  │                               #     ActTelemetry (the client registration, and the BeforeSend
│  │                               #     gate that drops an already-queued event once consent is
│  │                               #     withdrawn), PostHogTelemetrySink (sanitize → capture, and
│  │                               #     every failure swallowed into the disk log),
│  │                               #     NullTelemetrySink (what a build with no token gets),
│  │                               #     TelemetryOptions binds the "Telemetry" section — the
│  │                               #     switch AND a token are both required before a client exists
│  ├─ Act.App/                      # Blazor Server UI + Electron desktop host (ElectronNET.Core)
│  │  ├─ Components/
│  │  │  ├─ Pages/                  #     EVERY @page component and nothing else:
│  │  │  │                          #       BoardView "/", TaskView, SessionView,
│  │  │  │                          #       ArchiveView, TemplatesView, TemplateView,
│  │  │  │                          #       SettingsView, Error, NotFound
│  │  │  ├─ Board/                  #     non-routable board parts (FlightStrip)
│  │  │  ├─ Shared/                 #     CardTabs (Task|Terminal|Timeline), PathPicker,
│  │  │  │                         #       AttachmentPreview (the hover thumbnail both the task
│  │  │  │                         #         form's chips and the terminal rail render; its parent
│  │  │  │                         #         element is the anchor act-attach.place measures),
│  │  │  │                         #       EscapeKey (renders nothing; binds Escape for a page)
│  │  │  └─ Layout/                 #     MainLayout, top bar, theme stylesheets
│  │  ├─ Desktop/                   #   DesktopShell: the Electron window + tray icon and the
│  │  │                            #     close/exit rules (Electron-only; registered when enabled)
│  │  │                            #     + IDesktopBridge / INotifier and their two
│  │  │                            #     implementations each — the only shell surface a page may
│  │  │                            #     touch. With Program.cs and the DI extensions, the ONLY
│  │  │                            #     place allowed to name ElectronNET (a test enforces it)
│  │  ├─ Attachments/               #   the one route that serves a local file: a card's attached
│  │  │                            #     image, for the chip's hover thumbnail. Thin like Hooks/ —
│  │  │                            #     the image list is TaskAttachment's and the folder boundary
│  │  │                            #     is IAttachmentStore.ResolveInside, both tested web-free.
│  │  │                            #     Also AttachmentOpener: resolve a card's file name to a real
│  │  │                            #     path and hand it to the shell, answering with an outcome
│  │  │                            #     (opened / gone / refused) so the page owns the wording
│  │  ├─ Cards/                     #   BoardState — owns every card incl. archived ones, and
│  │  │                            #     decides what may see which; RetentionPump (the hourly
│  │  │                            #     auto-archive sweep); task form model (NewTaskForm, and
│  │  │                            #       its conversions to a Card and to a TaskTemplate);
│  │  │                            #     TaskLabels — the label/choice vocabulary every form
│  │  │                            #       that describes a task shares; capability
│  │  │                            #     catalog built from the registered adapters;
│  │  │                            #     TaskTitles (ask the task's agent to name it, and always
│  │  │                            #       answer — the prompt's opening words are the floor);
│  │  │                            #     AttachmentSweep (one startup pass clearing attachment
│  │  │                            #       folders no card claims)
│  │  ├─ Sessions/                  #   SessionRegistry (the live sessions), SessionLauncher
│  │  │                            #     (launch + restore), SessionRestorer (startup re-attach),
│  │  │                            #     CardCompleter / CardReopener (the two hand-driven moves
│  │  │                            #     past the launch boundary),
│  │  │                            #     SessionEventPump (drains events onto the board),
│  │  │                            #     TranscriptPump (one polling tail per live session),
│  │  │                            #     QueueRunner (the unattended launches + the sleep
│  │  │                            #       inhibitor; the board reads its holds so the chip and
│  │  │                            #       the decision are one evaluation),
│  │  │                            #     TerminalGeometry (last xterm size, for headless launches)
│  │  ├─ Notifications/             #   NotificationDispatcher (setting + focus gate + one ping
│  │  │                            #     per state, and the wording), UiPresence (who is looking
│  │  │                            #     at what), DeepLinkRouter (clicked toast → that card)
│  │  ├─ Usage/                     #   UsageState (latest result per agent, reading or named
│  │  │                            #     unavailability) + UsagePump (one poll loop per probe,
│  │  │                            #     backing off on a failure that cost a request, woken
│  │  │                            #     early when the credential file changes);
│  │  │                            #     UsageIndicator renders both in the top bar
│  │  ├─ Telemetry/                 #   ConsentedTelemetrySink (the front gate: the switch read on
│  │  │                            #     every capture, never once at startup), TelemetryPump (one
│  │  │                            #     app_started carrying the settings; a dispose that only
│  │  │                            #     flushes), TelemetryErrorBridge (an ILoggerProvider — where
│  │  │                            #     all four unhandled-exception layers converge, incl. Blazor's;
│  │  │                            #     reads the Exception and nothing else) + CrashReports (the cap
│  │  │                            #     and the last-chance flush, counted once)
│  │  ├─ Hosting/                   #   BackgroundWork: the lifetime every pump shares — one
│  │  │                            #     cancellation source, the single-flight gate, a guard per
│  │  │                            #     pass, and a shutdown that waits before it disposes.
│  │  │                            #     StartupLog: the facts a log file has to open with
│  │  │                            #     (version, mode, data dir, hook port) plus the
│  │  │                            #     unhandled-exception catch-all
│  │  ├─ Settings/                  #   user-settings service + culture
│  │  ├─ Resources/                 #   .resx strings (en / fr)
│  │  ├─ wwwroot/                   #   CSS (flight-strip look), assets
│  │  │  ├─ lib/xterm/              #     xterm.js + fit addon (vendored UMD) + VENDOR.md
│  │  │  │                         #       pinning version + SHA-256 of each file
│  │  │  └─ js/                     #     interop modules: act-terminal (attach / write / resize /
│  │  │                            #       dispose), act-unsaved (the window-close guard),
│  │  │                            #       act-escape (Escape leaves the page it is bound on),
│  │  │                            #       act-presence (focus + visibility, for the toast gate),
│  │  │                            #       act-attach (a drop or a file paste funnelled into the
│  │  │                            #         page's own InputFile, so all three attachment routes
│  │  │                            #         stream the same way; a text paste is never claimed)
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
│  ├─ Act.App.UiTests/              # the app layer: its services, plus a bUnit suite per component
│  │                                #   — ComponentTest builds the app graph over the fakes, and
│  │                                #   RadzenDom is the one place that names Radzen's rendered shapes
│  ├─ Act.App.E2eTests/             # Playwright against the real app on a real port, mock-adapter
│  │                                #   driven. ActApp is the host (two of them — see tests/README);
│  │                                #   scoped to what bUnit cannot see: the JS modules, xterm, and
│  │                                #   the round trips. Excluded from the default test run
│  └─ Act.TestSupport/              # the MOCK adapter + fixtures/builders (shared)
│
├─ scripts/
│  ├─ build.ps1                     # repeatable build + test (CI hook); cross-platform pwsh
│  ├─ package-desktop.ps1           # Electron.NET desktop packaging
│  └─ stamp-telemetry.ps1           # writes the PostHog token into appsettings.json (release.yml, both packaging jobs)
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
- **The agent↔ACT contract is Core's, not an adapter's** — a single MCP tool
  (`create_followup`), not a file convention. The tool's schema and handler belong to
  Core, and an adapter only decides how the MCP server is *declared* to its CLI
  (`--mcp-config` vs. a `--profile` table). See the spec's *Agent ↔ ACT contract*.
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
  | `/templates` | `TemplatesView` |
  | `/template/new` · `/template/{id}` | `TemplateView` |
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
  archive. Those two are the whole list — there is no completion/git prompt, so
  sign-off and reopen are plain drags. `RadzenComponents`
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
  `docs/findings/agent-usage.md`.
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
  - **Retiring a reason is a code change today, and will not always be.** The vocabulary
    changes as the rules do, and LiteDB's enum deserializer throws on a name the build no
    longer has. Before the first public release that costs nothing — the store is wiped —
    but once a real board exists, a retired name needs either a schema migration or a
    tolerant `RegisterType` in `ActBsonMapper` that reads it back as **null**, which
    `TransitionText` already renders as the transition's verbatim note.
- **The pseudo-terminal is a Core port, not adapter code.** Spawning a process under
  a pty and pumping its bytes is agent-agnostic plumbing; only the *command line* is
  agent-shaped. So `IPtyHost` lives in `Act.Core/Abstractions` and its `Porta.Pty`
  implementation in `Act.Infrastructure/Terminal/`, injected into each adapter. The
  alternative — adapters referencing `Act.Infrastructure` — would be a sideways
  dependency between two projects that are both supposed to point *inward*, and it
  would drag `Porta.Pty` into every adapter. It also keeps the contract suite
  process-free: a fake `IPtyHost` is all a test needs.
- **`ICommandHost` is the pty port's mirror image, and lands the same
  way.** Starting a process and capturing what it said is agent-agnostic
  plumbing, exactly like spawning one under a pty, so the port lives in
  `Act.Core/Abstractions` and its implementation beside `PtyHost` in
  `Act.Infrastructure/Terminal/` — they share the `PATH` walk and nothing else.
  What it exists for is the questions ACT asks a CLI **on its own account** rather
  than on the user's: today just "name this task". `QueryAsync` is on
  `IAgentAdapter` for the usual reason — the command line is agent-shaped — and the
  cheapest model to ask is declared as `AgentCapabilities.UtilityModel`, a second
  default the user never chooses. `StubCommandHost` keeps the adapter suites off the
  process table the way `StubPtyHost` does; `CommandHostTests` is the one suite that
  starts real processes, because a closed stdin, an undeadlocked stderr and a
  deadline that actually kills are properties of the OS and a fake would only prove
  the fake works. The shell it drives is the platform's own, never an agent CLI.
- **`IAttachmentStore` is declared beside its implementation, not in
  `Act.Core`.** A task's attached files live under `<dataDir>/attachments/<cardId>/`, and the
  port for reaching them sits in `Act.Infrastructure/FileSystem/AttachmentStore.cs` with the
  class that implements it. That is the `INotifier` line applied a second time: an interface
  earns a place in `Act.Core/Abstractions` only when Core-side code triggers it, and nothing in
  Core does — `AttachmentInstruction` takes resolved paths, and the adapters are handed
  `AgentAttachments` rather than a store, which is also what keeps the contract suite off the
  filesystem the way `IPtyHost` keeps it off the process table. Contrast
  `IWorkingDirectories`, which *is* a Core port because `WorkingDirConflict` really calls it.
  It also keeps `Act.TestSupport` on its single `Act.Core` reference: the fake lives in
  `Act.App.UiTests` beside `FakeCardStore`, which is the only suite that needs one.
- **The contract suite covers only the process-free surface.** `AgentAdapterContract`
  in `Act.Agents.Tests` asserts identity, capabilities and launch-config resolution —
  never a live session — because ACT deliberately runs no real CLI in automated
  tests. Live behavior is verified by hand against the pinned CLIs.
- **`INotifier` is an `Act.App/Desktop` port, not a Core one.** Nothing in Core
  calls it: the decision half is a pure rule
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
- **`Act.Desktop` is not a project.** The folder existed as a
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
- **Logging is `Microsoft.Extensions.Logging` everywhere and Serilog in exactly one
  folder.** Every call site takes `ILogger<T>` through the constructor and says what it
  means with a message template; `Act.Infrastructure/Logging/` is the only place that
  names the provider, and `ActLoggingTests` fails the build if anything else does — the
  same trick `ElectronBoundaryTests` plays on ElectronNET, and it caught a *comment*
  naming Serilog within the hour. Swapping provider is one file plus one package
  reference.
  - **`appsettings.json`'s `Logging:LogLevel` stays the only level config**, which is
    what makes that swap real. It also rules out `AddSerilog()`: that extension installs
    its own `LogLevel.Trace` filter for its provider, deliberately making Serilog's
    `MinimumLevel` the authority and bypassing the `Logging` section entirely. ACT
    registers `SerilogLoggerProvider` itself instead — see *Logging* in
    `docs/design-notes.md`, which also records why it goes through a DI factory rather
    than `ILoggingBuilder.AddProvider`.
  - **The correlation id is a scope, not a message field.** `ActLogScope.BeginTaskScope`
    is the one spelling of `Task` (the card number a user reads) and `Session` (the id
    the spec names), so a task's whole trace is greppable and no call site invents its
    own key. `ActLogFormatter` renders whatever scope is present as a bracketed group and
    writes nothing when there is none.
- **The cloud metrics are the same shape, one layer over.** `ITelemetrySink` is the port,
  `Act.Infrastructure/Telemetry/` is the only place that names PostHog, and
  `TelemetryContainmentTests` fails the build if anything else does — it caught two comments
  and a leaking extension-method name on the day it was written. What separates it from
  logging is that the payload is *closed*: `Act.Core/Telemetry/TelemetryEvents` is the only
  place a payload is built and `TelemetryPayload` drops anything undeclared, so the promise
  on the Diagnostics switch is enforced in Core rather than trusted at the call sites.
  **Telemetry is deliberately not an `ILogger` provider** — see *Telemetry* in
  `docs/design-notes.md` for why that was rejected, and for the two gates the opt-out needs.
- **The store owns its own conventions.** `Storage/` keeps one entry point
  (`ActDatabase.Open`) that configures the `BsonMapper` and applies the schema —
  a **version document plus an ordered migration list**, so `CurrentVersion` is
  derived from that list and a store written by a newer build is rejected instead
  of silently mangled. **The list is empty, and schema 1 is the release baseline** —
  every store the first release will ever see is created by it, so there is nothing
  older to migrate from. The machinery stays, so the next breaking change is one
  entry and a version bump.
  Collection names live in one place (`ActCollections`), and
  the friendly card `number` comes from a counter document that seeds itself
  above any card already stored.
- **Backward compatibility is a migration, never a mapper.** A read-time shim — a
  tolerant converter, a fallback property, a "temporary" mapper for what an older
  build wrote — is the one shape this store does not allow, and the rule is in
  `CLAUDE.md` as a hard one. Nothing ever tells you the last old document is gone,
  so the shim outlives the data it was written for and the store carries two shapes
  indefinitely. A `Migrations` entry that rewrites the documents at startup leaves
  exactly one shape on disk.
  - **A migration writes through the mapper, never by hand.** The shape it has to produce is
    whatever the *new* type serializes to, and the two things LiteDB does not take verbatim —
    `Id` becomes `_id` even on a nested object, and an enum's encoding is the mapper's choice —
    are exactly what a hand-built `BsonDocument` gets wrong. That is why `ActSchema.Apply` takes
    the `BsonMapper` `ActDatabase` built the database with and hands it to every entry: read the
    old document straight into the new type and write the result back with `ToDocument`. A
    round-trip test in `SettingsStoreTests` is what pins the `_id` half of that, because nothing
    in the type declaration says it.
  - **A migration must be a no-op on a document it does not recognise**, not a throw: a fresh
    store has no settings document at all, so an invariant a new shape carries (there is
    always one default template) is seeded by `UserSettingsService` on the way in and a
    migration only rewrites what it finds.
  - **The mapper's defaults are part of the on-disk shape.** `EmptyStringToNull` defaults to
    `true`, which silently turned every stored empty string into null and every non-nullable
    string property into null on the way back — see *Empty strings were stored as null* in
    `docs/design-notes.md`. `ActBsonMapper` is the one place those defaults are decided, and a
    change to one of them is a store change needing a migration like any other.
  **Removing a migration takes `CurrentVersion` backwards, and any store already
  stamped above it can no longer be opened** — the newer-build guard fires and the only
  way out is deleting `act.db`. That is the price the reset to schema 1
  charged every pre-release store, and it is why after the first release the list only
  ever grows.
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

- **ACT writes nothing into a task's working directory.** Everything agent-facing —
  spawning included — goes through hooks and the MCP server, and generated launch
  config (hook settings, Codex profile, MCP config) lives in ACT's own data directory.
- The **`prototype` branch** (commit `bb7633d`) is reference material for the PTY
  work, not something to merge — it also carries a throwaway `Act.App/Prototype/`
  page and its `Program.cs` wiring. Read individual files with
  `git show prototype:<path>`; `PtySession.cs` and `wwwroot/js/prototype-pty.js` are
  the two worth lifting, and `PtyStateProbe.cs` is the one to leave behind (it exists
  to prove screen-scraping doesn't work).
- `LICENSE.md` is added by hand (copy FSL text from fsl.software); GitHub's
  license picker only lists OSI-approved licenses, so choose "No license" there.
