# Third-Party Notices

ACT bundles third-party components under permissive licenses (MIT / Apache 2.0 /
BSD). This file preserves their copyright and license notices — attribution is the
main permissive-license obligation, and it is one ACT is subject to on both sides:
the installer redistributes binaries it did not write.

Versions are pinned in `Directory.Packages.props` rather than restated here, except
where a component is vendored or redistributed and the exact version is part of the
notice.

## Referenced packages

Nothing in this section is vendored — the packages are restored from NuGet, so
their license texts travel with them.

- .NET / ASP.NET Core / Blazor — MIT, © Microsoft. `Microsoft.Extensions.*`
  (dependency injection, logging, options) and the Blazor runtime.
- Radzen.Blazor — MIT, © Radzen Ltd. (<https://www.radzen.com/>) — the UI library.
- LiteDB — MIT, © Maurício David (<https://www.litedb.org/>) — the embedded store.
- Porta.Pty — MIT, © Tom Laird-McConnell (<https://github.com/tomlm/Porta.Pty>) —
  the pseudo-terminal host.
- ElectronNET.Core / ElectronNET.Core.AspNet — MIT, © Gregor Biswanger, Florian
  Rappl, softworkz (<https://github.com/ElectronNET/Electron.NET/>) — the desktop
  shell bridge. What it *packages* is attributed under **Desktop runtime** below.
- PostHog — MIT, © PostHog — the product-analytics client.
- Serilog — **Apache 2.0**, © Serilog Contributors (<https://serilog.net/>) — the
  logging provider. Three packages: `Serilog`, `Serilog.Extensions.Logging`,
  `Serilog.Sinks.File`.
- ModelContextProtocol — **Apache 2.0**
  (<https://github.com/modelcontextprotocol/csharp-sdk>) — the MCP server ACT
  exposes to its agents. `ModelContextProtocol.AspNetCore` in the app,
  `ModelContextProtocol.Core` in the browser tests, which drive it with the SDK's
  own client.
- Microsoft.CodeAnalysis.ResxSourceGenerator — MIT, © Microsoft
  (<https://github.com/dotnet/roslyn>) — build-time only, but its output is
  compiled into the shipped assemblies.

Serilog and ModelContextProtocol are Apache rather than the MIT this list otherwise
runs on, so they are called out rather than left to be assumed.

## Vendored (source-committed, not a package reference)

These ship as files in the repository, so their notices must travel with them:

- **xterm.js** — MIT, © 2014 The xterm.js authors; © 2012–2013 Christopher Jeffrey.
  `@xterm/xterm` **5.5.0** and `@xterm/addon-fit` **0.10.0**, unmodified upstream
  `dist` files in `src/Act.App/wwwroot/lib/xterm/`. Versions, SHA-256 hashes and a
  re-verification script are pinned in that folder's `VENDOR.md`; the full license
  text is in the header of `xterm.css`.

## Desktop runtime (redistributed in the installer)

The NSIS installer and the Linux AppImage carry a full browser runtime, downloaded
and packaged by ElectronNET.Core at build time rather than committed here:

- **Electron 30.4.0** — MIT, © Electron contributors; © 2013–2020 GitHub Inc.
  (<https://github.com/electron/electron>). It embeds **Chromium**
  (BSD-3-Clause and the many licenses of its own dependencies) and **Node.js**
  (MIT).
- Electron ships `LICENSE.electron.txt` and `LICENSES.chromium.html` beside the
  executable, and electron-builder copies both into the packaged app. **That is the
  mechanism by which those notices reach the user — do not strip them from the
  packaged output**, and check they are still there after any change to
  `electron-builder.json` or the release workflow.
- The generated Electron host also bundles four npm modules, all MIT:
  `electron-updater`, `socket.io`, `image-size`, `dasherize`.

## Test-only

Not redistributed — these are restored for the test projects and never reach a
shipped artifact. Listed for completeness:

- xUnit (`xunit`, `xunit.runner.visualstudio`) — **Apache 2.0**
- AwesomeAssertions — **Apache 2.0**, © AwesomeAssertions, Dennis Doomen, Jonas
  Nyrup and contributors
- NSubstitute — **BSD-3-Clause**, © Anthony Egerton, David Tchepak, Alexandr
  Nikitin, Oleksandr Povar
- bunit — MIT, © Egil Hansen
- Microsoft.Playwright, Microsoft.AspNetCore.Mvc.Testing, Microsoft.NET.Test.Sdk —
  MIT, © Microsoft
- coverlet.collector — MIT

## No GPL/AGPL dependencies

They would conflict with ACT's source-available license. Re-check any new
dependency's license before adding it — including transitively, and including the
npm side of the Electron host.
