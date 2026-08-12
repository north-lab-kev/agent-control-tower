# Act.App

**Blazor Server UI — the composition root and the desktop host.** Owns DI
registration, Radzen setup, the board, and (via ElectronNET.Core) the Electron
desktop shell.

## Contents

- `Components/` — `Pages/` holds every routable view (board, task, session, timeline, templates, archive, settings), `Board/` the flight strip, `Shared/` the reusable pieces (card tabs, path picker, attachment preview).
- `wwwroot/` — CSS (the flight-strip / control-room look), the vendored xterm bundle, assets.
- `Program.cs` — startup / DI registration (Razor Components + `InteractiveServer`); wires Electron only when enabled.
- `Properties/electron-builder.json` — desktop packaging config (Windows NSIS installer + Linux AppImage). Committed; auto-seeded by ElectronNET.Core.

## Run — web (fast dev loop)

```
dotnet run --project src/Act.App --launch-profile http
```

Serves at `http://localhost:5210`. No Electron, no Node — plain Blazor Server.

`Development` runs are isolated from the installed app: port 5210 against
`%LOCALAPPDATA%\ACT.Development`, while `Production` keeps port 5200 and
`%LOCALAPPDATA%\ACT`. Both can run at the same time; see *Where the data lives* in
`docs/spec/persistence.md`.

## Run — Electron desktop window

```
dotnet run --project src/Act.App --launch-profile electron
```

The `electron` profile sets `Electron__Enabled=true`, which is the switch
`Program.cs` reads to wire `AddElectron()` / `UseElectron()` and open a native
window. Without it the app is a pure web server, so `dotnet build`, `dotnet run`,
and the Playwright UI tests never touch Node/Electron.

## Package the desktop artifact

```
scripts/package-desktop.ps1            # win-x64 (or -Rid linux-x64)
```

Wraps `dotnet publish -r <rid> -p:ElectronPackaging=true`; output lands in
`artifacts/desktop/<rid>/`. The `ElectronPackaging` flag is what keeps the RID
(needed by electron-builder); ordinary builds stay RID-agnostic and
cross-platform.

Releases add `-Version 1.2.3` (see `.github/workflows/release.yml`): that single
property is both what the running app reports (`AssemblyInformationalVersion`,
logged by `Hosting/StartupLog`) and what names the installer
(`ACT-Setup-<version>-win-x64.exe`, `ACT-<version>-linux-x86_64.AppImage`).

## UI direction

Air-traffic-control aesthetic; cards are flight-progress strips. Five flat columns,
launch-boundary shown dynamically at drag time (gray out invalid columns).
Compact/detailed density toggle. See
[../../docs/spec/ui-direction.md](../../docs/spec/ui-direction.md).

Signature flight-strip look = custom Blazor markup + CSS; heavier widgets
(dialogs, tables, inputs) = Radzen themed to the same palette.
