# Vendored xterm.js

Unmodified upstream `dist` files, committed rather than pulled at build time so the app has no
Node dependency at runtime and no CDN dependency at all (ACT runs locally, possibly offline).

The minified bundles carry **no version string**, so the version lives here or nowhere.

| File | Package | Version | SHA-256 |
|---|---|---|---|
| `xterm.js` | [`@xterm/xterm`](https://www.npmjs.com/package/@xterm/xterm) (`lib/xterm.js`) | **5.5.0** | `1F991AC3B4B283EBF96E60AE23A00A52765DD3A2E46FA6FDDA9F1AAB032F7495` |
| `xterm.css` | `@xterm/xterm` (`css/xterm.css`) | **5.5.0** | `BA8E6985669488981CCF40C0CEFE3ABA80722CB6C92DE7AD628B0BD717FAF2B6` |
| `addon-fit.js` | [`@xterm/addon-fit`](https://www.npmjs.com/package/@xterm/addon-fit) (`lib/addon-fit.js`) | **0.10.0** | `BDAEFA370B1BFC42EE88D46FE6072400902A4D4B2D45CD93438DDA9B23C97089` |

License: **MIT** (xterm.js authors; see the header in `xterm.css`) — recorded in
`THIRD-PARTY-NOTICES.md`.

Note `xterm.css` is byte-identical in 5.4.0 and 5.5.0, so the CSS hash alone does not pin the
version; `xterm.js` does.

## Verifying these are pristine

Hashes were checked against the published packages on 2026-07-29 and matched exactly. To
re-check, or after an upgrade:

```powershell
$v='5.5.0'; $f='0.10.0'
$t=Join-Path $env:TEMP "xterm-verify"; New-Item -ItemType Directory -Force $t | Out-Null
@(
  @{u="https://cdn.jsdelivr.net/npm/@xterm/xterm@$v/lib/xterm.js";      n='xterm.js'},
  @{u="https://cdn.jsdelivr.net/npm/@xterm/xterm@$v/css/xterm.css";     n='xterm.css'},
  @{u="https://cdn.jsdelivr.net/npm/@xterm/addon-fit@$f/lib/addon-fit.js"; n='addon-fit.js'}
) | ForEach-Object {
  Invoke-WebRequest $_.u -OutFile (Join-Path $t $_.n) -UseBasicParsing
  $up=(Get-FileHash (Join-Path $t $_.n) -Algorithm SHA256).Hash
  $me=(Get-FileHash (Join-Path $PSScriptRoot $_.n) -Algorithm SHA256).Hash
  "{0,-14} {1}" -f $_.n, $(if ($up -eq $me) {'MATCH'} else {"DIFFERS`n  upstream $up`n  local    $me"})
}
```

## Upgrading

Replace all three files together, update the versions and hashes above, and re-run the check.
Then exercise the session view by hand: `act-terminal.js` leans on `FitAddon.proposeDimensions`
and on the DOM renderer painting into `.xterm-rows`, neither of which is covered by a test.
