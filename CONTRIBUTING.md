# Contributing to ACT

*Placeholder — to be expanded.*

## Prerequisites

- **.NET 10 SDK** — pinned in [`global.json`](global.json). Verify with `dotnet --version`.
- **Node.js** — LTS (22.x or newer). Required by the Electron desktop shell:
  ElectronNET.Core stages the Electron runtime while building `Act.App`, and
  packaging uses electron-builder. Verify with `node --version`.

  > This is currently needed for **any** build of `Act.App` (including the web
  > dev loop and tests), because the Electron build steps run on every build.

### Installing Node.js

**Windows** (winget ships with Windows 11):

```bash
winget install OpenJS.NodeJS.LTS
```

**macOS** (Homebrew):

```bash
brew install node
```

**Linux** (nvm — distro-independent; check the [nvm repo](https://github.com/nvm-sh/nvm) for the latest installer tag):

```bash
curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.1/install.sh | bash && nvm install --lts
```

Restart your shell afterwards so `node`/`npm` land on `PATH`.

### Packaging note (Windows)

Producing the desktop artifact on Windows needs the symbolic-link privilege for
electron-builder's code-sign toolchain. Enable **Developer Mode** (Settings →
Privacy & security → For developers) or run the packaging from an **elevated**
shell.

## Ground rules

- Respect the architecture: `Act.Core` depends on nothing below it. Adapters,
  infrastructure, and UI depend on Core's `Ports/` interfaces — never the
  reverse. Adding an agent must not touch core logic.
- Use the spec's **domain vocabulary** verbatim in code (cards, columns, badges,
  transitions, adapters, sources) so spec and code stay legible together.
- Nullable reference types on; warnings-as-errors on the core projects;
  `dotnet format` enforced.
- Tests: xUnit + AwesomeAssertions / Shouldly + NSubstitute. **Do not use
  FluentAssertions v8+** (commercial license).

## CLA / DCO

Contributions are accepted under a Contributor License Agreement / Developer
Certificate of Origin (protects future dual-licensing). *Exact mechanism TBD.*

See [docs/ACT-overview.md](docs/ACT-overview.md) for the spec and
[docs/ACT-roadmap.md](docs/ACT-roadmap.md) for the build sequence.
