# scripts

Repeatable build & packaging scripts (the CI build hook).

Cross-platform PowerShell (`pwsh`) scripts — they run on Windows and on Linux/CI
(GitHub's Ubuntu runners ship with `pwsh`).

- **`build.ps1`** — restore, build, and test the whole solution (`Act.slnx`).
  Defaults to `Release`; override with `-Configuration Debug`. This is the
  command CI runs (`pwsh scripts/build.ps1`).
- **`package-desktop.ps1`** — publish the Electron desktop artifact for a runtime
  (`-Rid win-x64`, default). Output lands in `artifacts/desktop/<rid>/`. Needs
  Node.js; on Windows also needs symlink privilege (Developer Mode or an elevated
  shell) — see [../CONTRIBUTING.md](../CONTRIBUTING.md#prerequisites).
  `-Version 1.2.3` stamps the release version: it becomes `$(Version)`, which the
  SDK writes into `AssemblyInformationalVersion` (what the running app reports)
  and ElectronNET.Core forwards to electron-builder, so the installer is named
  and versioned from it too. Omit it for a local build (`1.0.0`).
