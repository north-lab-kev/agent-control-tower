# Third-Party Notices

**Placeholder — to be populated before the first public release.**

ACT bundles third-party components under permissive licenses (MIT / Apache 2.0 /
BSD). This file preserves their copyright and license notices (attribution is the
main permissive-license obligation).

Expected dependencies to attribute (fill in as they are added):

- .NET / ASP.NET Core / Blazor — MIT
- Radzen.Blazor — MIT
- Electron.NET — MIT
- LiteDB — MIT
- Porta.Pty — MIT (<https://github.com/tomlm/Porta.Pty>) — the pseudo-terminal host
- Serilog — Apache 2.0, © Serilog Contributors (<https://github.com/serilog/serilog>) —
  the logging provider, added 2026-08-04. Three packages: `Serilog`,
  `Serilog.Extensions.Logging`, `Serilog.Sinks.File`. Nothing is vendored, so the notices
  travel with the packages; versions are pinned in `Directory.Packages.props` rather than
  restated here.
- ModelContextProtocol — **Apache 2.0** (<https://github.com/modelcontextprotocol/csharp-sdk>) —
  the MCP server ACT exposes to its agents, added 2026-08-06. Two packages:
  `ModelContextProtocol.AspNetCore` in the app and `ModelContextProtocol.Core` in the browser
  tests, which drive it with the SDK's own client. Apache rather than the MIT this list
  otherwise runs on, so it is called out here; nothing is vendored, and versions are pinned in
  `Directory.Packages.props`.
- xUnit, AwesomeAssertions / Shouldly, NSubstitute (test-only) — Apache 2.0 / MIT

**Vendored (source-committed, not a package reference)** — these ship as files in
the repository, so their notices must travel with them:

- **xterm.js** — MIT, © 2014 The xterm.js authors; © 2012–2013 Christopher Jeffrey.
  `@xterm/xterm` **5.5.0** and `@xterm/addon-fit` **0.10.0**, unmodified upstream
  `dist` files in `src/Act.App/wwwroot/lib/xterm/`. Versions, SHA-256 hashes and a
  re-verification script are pinned in that folder's `VENDOR.md`; the full license
  text is in the header of `xterm.css`.

**No GPL/AGPL dependencies** — they would conflict with ACT's source-available
license. Re-check any new dependency's license before adding it.
