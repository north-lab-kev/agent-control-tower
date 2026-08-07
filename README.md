# ACT — Agent Control Tower

A column-based (Kanban) control surface that shows all your coding-agent
sessions (Claude Code first, Codex later) and their states at a glance, so you
can spot which ones need your attention. The name doubles as the verb *to act*:
the tool's whole job is helping you decide which session needs attention — and
act on it.

## Status

Early scaffolding. The design is complete; implementation has not started. See:

- [docs/overview.md](docs/overview.md) — the full specification
- [docs/roadmap.md](docs/roadmap.md) — the 16-step build plan
- [docs/repository-structure.md](docs/repository-structure.md) — this layout, explained

## Tech stack

| Layer | Choice |
|---|---|
| Runtime / UI | .NET 10 (LTS) + Blazor Server |
| UI components | Radzen.Blazor (free) |
| Desktop shell | Electron.NET (thin window) |
| Data store | LiteDB (embedded, single-file) |
| Ingestion | Pluggable multi-source (HTTP/command hooks, FileSystemWatcher, process signals, stream-json control channel) |

## Architecture

Ports-and-adapters (hexagonal). `Act.Core` depends on nothing infrastructural;
adapters, infrastructure, and UI all point *inward* to its `Abstractions/` interfaces.
**The project boundaries are the architecture.**

## Development

**Prerequisites:** the **.NET 10 SDK** and **Node.js** (LTS). Node is required
because the Electron desktop shell stages its runtime during the build — see
[CONTRIBUTING.md](CONTRIBUTING.md#prerequisites) for one-line install commands
per OS.

```bash
dotnet run --project src/Act.App --launch-profile http
```

Serves the browser dev loop at `http://localhost:5210`. The Electron.NET desktop
build is a packaging step over the same app (`scripts/package-desktop.ps1`).

## License

**Functional Source License (FSL)** — source-available, not OSI "open source".
Each release auto-converts to Apache 2.0 after two years. See
[LICENSE.md](LICENSE.md). *Not legal advice.*
