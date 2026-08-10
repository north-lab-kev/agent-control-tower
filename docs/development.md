# Development

Everything a developer needs to build and run ACT. For what ACT *is*, read
[overview.md](overview.md) — the authoritative spec.

## Status

Early development, pre-release. The design is complete and implementation is
under way — see [roadmap.md](roadmap.md) for the build sequence and each step's
status, and [repository-structure.md](repository-structure.md) for the project
layout, explained.

## Tech stack

| Layer | Choice |
|---|---|
| Runtime / UI | .NET 10 (LTS) + Blazor Server |
| UI components | Radzen.Blazor (free) |
| Desktop shell | Electron.NET (thin window) |
| Data store | LiteDB (embedded, single-file) |
| Terminal | Porta.Pty (ConPTY / Unix pty) + xterm.js |
| Ingestion | Pluggable multi-source (HTTP/command hooks, FileSystemWatcher, process signals) |

## Architecture

Ports-and-adapters (hexagonal). `Act.Core` depends on nothing infrastructural;
adapters, infrastructure, and UI all point *inward* to its `Abstractions/`
interfaces. **The project boundaries are the architecture.** See
[repository-structure.md](repository-structure.md) for the full layout and the
dependency direction.

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

### Packaging note (Windows)

Producing the desktop artifact on Windows needs the symbolic-link privilege for
electron-builder's code-sign toolchain. Enable **Developer Mode** (Settings →
Privacy & security → For developers) or run the packaging from an **elevated**
shell.

## Ground rules

- Respect the architecture: `Act.Core` depends on nothing below it. Adapters,
  infrastructure, and UI depend on Core's `Abstractions/` interfaces — never the
  reverse. Adding an agent must not touch core logic.
- Use the spec's **domain vocabulary** verbatim in code (cards, columns, badges,
  transitions, adapters, sources) so spec and code stay legible together.
- Nullable reference types on; warnings-as-errors on the core projects;
  `dotnet format` enforced.
- Tests: xUnit + AwesomeAssertions / Shouldly + NSubstitute. **Do not use
  FluentAssertions v8+** (commercial license).
