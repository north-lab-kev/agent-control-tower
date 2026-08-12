# Development

Everything a developer needs to build and run ACT. For what ACT *is*, read
[overview.md](overview.md) — the authoritative spec.


## Tech stack

Constraints driving these choices: runs **locally**, cross-OS.

| Layer | Choice | Why |
|---|---|---|
| Runtime / UI | .NET 10 (LTS) + Blazor Server | Blazor Server's usual weakness (SignalR circuit latency) disappears on localhost, and it gives live push to the board for free. |
| UI components | Radzen.Blazor (free NuGet package) | 145+ native C# components, MIT-licensed. Only the free library — *not* the paid Radzen Blazor Studio. Requires `InteractiveServer` render mode. |
| Desktop shell | Electron.NET (thin window) | A native desktop window that feels more finished than a browser tab; Linux supported (glibc 2.31+). |
| Data store | LiteDB (embedded, single-file) | C#-native document database, zero install, no server. |
| Terminal | Porta.Pty (ConPTY / Unix pty) + xterm.js | ACT hosts the agent's real interactive TUI: raw bytes both ways over the Blazor circuit, batched ~30 ms with a capped buffer — fine on localhost. |
| Ingestion | Pluggable multi-source (HTTP/command hooks, transcript tail, process signals) | Adapters compose a mix per agent; all normalize into one event stream, purely observational. |

## Architecture

Ports-and-adapters (hexagonal). `Act.Core` depends on nothing infrastructural;
adapters, infrastructure, and UI all point *inward* to its `Abstractions/`
interfaces. **The project boundaries are the architecture.** See
[repository-structure.md](repository-structure.md) for the full layout and the
dependency direction.

ACT is a **clean, standalone ASP.NET Core + Blazor Server app**, and Electron is
nothing but a **thin window** around it. Keeping the app Electron-agnostic means
`dotnet run` stays the fast dev loop, the desktop build is just a packaging
step, and the shell could be swapped without touching ACT itself.

- **Ports as interfaces.** The agent adapter, the pseudo-terminal, the event sink
  every observer pushes into, and the store are ports behind interfaces; concrete
  adapters (Claude Code, Codex, …) and the store (LiteDB) are plug-ins. Adding an
  agent must not touch core logic.
  - **Ingestion is push, not a port of its own** — see the spec's *Pluggable,
    multi-source ingestion*.
- **Dependency injection** via the built-in .NET DI container; nothing news-up
  its own dependencies across a layer boundary.

## Prerequisites

- **.NET 10 SDK** — pinned in [`global.json`](../global.json). Verify with
  `dotnet --version`.
- **Node.js** — LTS (22.x or newer). Required because the Electron desktop
  shell stages its runtime during the build; this currently applies to **any**
  build of `Act.App`, including the web dev loop and tests. Verify with
  `node --version`.

### Installing Node.js

**Windows** (winget ships with Windows 11):

```bash
winget install OpenJS.NodeJS.LTS
```

**Linux** (nvm — distro-independent; check the [nvm repo](https://github.com/nvm-sh/nvm) for the latest installer tag):

```bash
curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.1/install.sh | bash && nvm install --lts
```

Restart your shell afterwards so `node`/`npm` land on `PATH`.

## Running

The default launch profile is the Electron desktop shell — the same way the
packaged app runs:

```bash
dotnet run --project src/Act.App --launch-profile electron
```

For a browser-only dev loop, use the `http` profile instead, which serves the
app at `http://localhost:5210`:

```bash
dotnet run --project src/Act.App --launch-profile http
```

The distributable desktop installer is a packaging step over the same app
(`scripts/package-desktop.ps1`).

### Packaging notes

- Producing the desktop artifact on Windows needs the symbolic-link privilege for
  electron-builder's code-sign toolchain: enable **Developer Mode** (Settings →
  Privacy & security → For developers) or run the packaging from an **elevated**
  shell.
- Building Linux packages from Windows needs **WSL2**.
- The no-CLI, MSBuild-integrated Electron.NET experience ("ElectronNET.Core") is
  still **pre-release**; the stable classic path works but uses the older CLI
  flow.

## Testing (fast, deterministic, no real CLIs)

- **Framework:** **xUnit**, with **AwesomeAssertions** (the free Apache-2.0
  community fork of FluentAssertions — FA v8+ went commercial) or
  **Shouldly** for readable assertions, and **NSubstitute** for mocking
  dependencies at the ports. *(Do not use FluentAssertions v8+ — paid license.)*
- **Unit tests — the rules engine hard.** It's pure logic (event in →
  column/badge out); test it exhaustively with no processes, files, or agents.
  Highest-value surface.
- **Mock adapter as the lifecycle fixture.** Drive whole task
  lifecycles — scheduling, spawning, sign-off — through scripted fake
  events, deterministically, no real CLI or tokens.
- **Contract tests.** One shared xUnit suite that **every** agent adapter must
  pass, so Claude Code and Codex are held to the same normalized behavior.
- **Component tests — bUnit**, one suite per component, rendering in-process
  against an AngleSharp DOM with no server and no browser. This is where UI
  behaviour is pinned: computed values reaching the elements that consume them,
  gestures reaching the right handlers, which branch rendered and what is absent.
  Everything a render can answer belongs here rather than in a browser.
- **UI tests — Playwright for .NET** against the **browser-mode** Blazor app,
  driven by the **mock adapter** so flows are deterministic. Run against browser
  mode, **not** the Electron shell (single-instance-lock / CDP friction).
  Deliberately narrow, because bUnit already covers the components: the **five JS
  interop modules** (which bUnit stubs and therefore cannot see at all), **xterm**,
  and **round trips** — the circuit, the store on disk, and a card moving on
  screen because an agent event arrived. See `../tests/README.md`.
- **Coverage:** **coverlet**; weighted — domain core / rules engine ~90%+; no
  blanket 100% mandate elsewhere.
- **No automated integration tests against real CLIs** (deliberate — slow,
  flaky, token-costly). Real-CLI behavior is verified **manually**, feature by
  feature, against the pinned CLI versions.

## CI

- **GitHub Actions**, running the **full fast suite** (unit + contract + bUnit
  component tests) on every push / PR, on a **Windows + Linux** matrix. Blocking,
  and `fail-fast: false` so both legs report.
- **The Playwright suite runs as its own `e2e` job**, also blocking — build,
  install Chromium, then test, in that order: the installer is a build artefact
  (`Microsoft.Playwright` emits `playwright.ps1` into the test output, which pins
  the browser to the package version), so the test step runs `build.ps1 -NoBuild`.
  Separate so the fast suite still reports in seconds and a browser failure is
  legible on its own. `scripts/build.ps1` excludes the category by default, so a
  local `dotnet test` stays fast and never needs a browser; `-E2E` runs only it.
  If `e2e` ever starts flaking, move it to `schedule` + `workflow_dispatch` rather
  than `continue-on-error`, which hides it instead.
- **Why the fast suite pays for two legs and `e2e` does not.** A handful of tests
  return early off Windows (the `cmd.exe` shim, the UTF-8 stdin code page, a
  refused write) and a dozen more assert whatever the running platform spells
  (`WindowsArgument` quoting, `ExecutableResolver`'s `PATHEXT` probing, the Codex
  forwarder) — on Linux alone the spawn layer of the platform the installer targets
  is verified nowhere. The browser tier has no such asymmetry: it drives Blazor in
  Chromium, and even the Windows clipboard is covered by synthesising the measured
  shape in the page, so a second leg would re-run identical assertions behind
  another Release build and browser download.
- **`release.yml` gates the same way.** `gate` validates the version, the tag and
  the telemetry secret in seconds; a `test` job then runs the matrix, and both
  packaging jobs `need` it — so no installer is built from a commit that was only
  green on Linux. The tests are re-run there rather than trusted from `ci`, because
  a release is dispatched against whatever ref the operator chose.
- macOS is not built or tested (nothing ships for it); it would bill at 10× if it
  ever did.
- The release-build script (`scripts/build.ps1`) is the CI build hook.

## Observability

- **Structured logging** through `Microsoft.Extensions.Logging`, written to a **daily
  rolling file** in ACT's own data directory (`<data>/logs/act-YYYYMMDD.log`, kept a
  fortnight, reachable from Settings → *Diagnostics*). **Serilog** is the provider and is
  confined to one folder, so `appsettings.json`'s `Logging:LogLevel` stays the only level
  config and the provider is genuinely replaceable — a test enforces the boundary.
  - **The correlation id is a log scope**, so a task's whole trace is filterable —
    near-essential when orchestrating opaque subprocesses. It carries the `sessionId`
    *and* the card number, because the number is the identifier a user
    can read off a strip and quote back. What is instrumented is session lifetime and
    every failure path: startup facts, schema migrations, launch/retry/restore/restart
    with their refusals, rules-engine moves, sign-off and reopen, queue arms and holds,
    session end, and an unhandled-exception catch-all.
- **No silent failures** — matches the spec's ethos (explicit fallbacks: the
  `to review` status fallback, error→retry). Surface errors to
  the card/badge, never swallow.

## Dependency licensing

- All shipped dependencies are **permissive** (MIT / Apache 2.0 / BSD) — .NET,
  Blazor, Radzen.Blazor, Electron.NET, LiteDB, Serilog, Porta.Pty, xterm.js, etc. —
  so they combine freely into a work distributed under ACT's source-available
  license. **No GPL/AGPL dependencies** (would conflict with a non-open license).
  `Porta.Pty` and xterm.js are both **MIT** and are recorded in the NOTICES file.
- **Vendored files carry an extra duty.** xterm.js ships as committed `dist` files
  rather than a package reference — deliberately, so the app needs neither Node nor a
  CDN at runtime — which means nothing resolves or records its version for us. A
  `VENDOR.md` beside the files pins the version and SHA-256 of each, and carries the
  script that re-verifies them against the published package. Do the same for anything
  else vendored: an un-versioned blob in `wwwroot` is a supply-chain hole.
- Obligation: ship a **third-party NOTICES file** preserving the dependencies'
  copyright/license notices (attribution is the main permissive-license duty).
- Keep test-only tooling free: **avoid FluentAssertions v8+** (commercial); use
  AwesomeAssertions / Shouldly. Re-check any new dependency's license before
  adding it.

## Ground rules

- Respect the architecture: `Act.Core` depends on nothing below it. Adapters,
  infrastructure, and UI depend on Core's `Abstractions/` interfaces — never the
  reverse. Adding an agent must not touch core logic.
- Use the spec's **domain vocabulary** verbatim in code (cards, columns, badges,
  transitions, adapters, sources) so spec and code stay legible together.
- Nullable reference types on; warnings-as-errors on the core projects;
  .NET analyzers enabled; `dotnet format` enforced — consistency is tooling's
  job, not willpower's.
- **Readable and self-documenting** — clear names, small focused functions and
  classes, single responsibility. **SOLID** especially at the port boundaries:
  they are what make the adapters swappable and the tests deterministic.
- **Comment the *why*, not the *what*** — and explicitly flag the deliberately
  odd, load-bearing bits (the PTY's incremental UTF-8 decode and batched flush,
  atomic temp→rename file writes, the launch-boundary rule) so a future reader
  doesn't "tidy away" a quirk that matters.
- **DRY with judgment** — don't over-abstract. The two adapters will look
  similar without being the same; premature deduplication there fights the
  abstraction.
