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
│  │  ├─ Events/                    #   normalized event types
│  │  ├─ Rules/                     #   the rules engine (pure logic)
│  │  ├─ Scheduling/                #   queue runner, schedule, backpressure
│  │  └─ Abstractions/              #   INTERFACES: IAgentAdapter, IIngestionSource,
│  │                               #     ITaskStore, INotifier, IClock…
│  ├─ Act.Agents.ClaudeCode/        # Claude Code adapter (launch, stream-json control, mappings)
│  ├─ Act.Agents.Codex/             # Codex adapter
│  ├─ Act.Infrastructure/           # LiteDB store, localhost hook host, FileSystemWatcher,
│  │                               #   process supervision, Serilog wiring
│  ├─ Act.App/                      # Blazor Server UI + Electron desktop host (ElectronNET.Core)
│  │  ├─ Components/                #   board, flight strips, drawer, new-task modal
│  │  ├─ wwwroot/                   #   CSS (flight-strip look), assets
│  │  ├─ Properties/                #   launchSettings + electron-builder.json (packaging)
│  │  └─ Program.cs                 #   startup / DI; Electron wired only when enabled
│  └─ Act.Desktop/                  # unused under Option B (Electron lives in Act.App); pending removal
│
├─ tests/
│  ├─ Act.Core.Tests/               # xUnit — rules engine & scheduler (hard, pure)
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
  deterministic sessions through it.
- **`INotifier` is a port.** Native OS notifications come from Electron, which
  under Option B lives in `Act.App` — so the native implementation lives there
  too (gated on Electron being active), with a no-op/web fallback for the plain
  `dotnet run` browser mode. *(Originally slated for `Act.Desktop`; moved when
  Electron wiring was consolidated into `Act.App`.)*
- **Node artifacts must be git-ignored** — Electron.NET pulls in `node_modules`
  and a build-output folder; easy to accidentally commit (bloat + secrets risk
  on go-public).
- **Solo-project judgment calls:** domain and application are merged into
  `Act.Core` (not split); adapters are self-contained with no shared
  `Act.Agents.Common` until real shared plumbing emerges (DRY with judgment).

## Notes

- The `.act/` directories (`status/`, `followups/`) are **not** part of the
  repo — they're created at runtime inside each task's working directory.
- `LICENSE.md` is added by hand (copy FSL text from fsl.software); GitHub's
  license picker only lists OSI-approved licenses, so choose "No license" there.
