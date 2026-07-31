# Agent usage — where the 5-hour and weekly numbers come from

**Status: resolved.** Both agents expose a live HTTP usage endpoint, and ACT reads
both. Measured 2026-07-31 against `claude-code 2.1.220` and the Codex CLI at
`AppData\Local\OpenAI\Codex\bin\…\codex.exe`.

This supersedes the roadmap's old open question, *"`/usage` scriptability +
reset-time detection"*. It is not scriptable — and it does not need to be.

Companion reading: *Usage indicator* in `ACT-overview.md`, and the usage rows in
`repository-structure.md`.

---

## The two endpoints

| | Claude Code | Codex |
|---|---|---|
| Endpoint | `https://api.anthropic.com/api/oauth/usage` | `https://chatgpt.com/backend-api/wham/usage` |
| Method | `GET` | `GET` |
| Auth | `Authorization: Bearer <access token>` | `Authorization: Bearer <access token>` |
| Other headers | **none required** | **none required** |
| Credentials | `~/.claude/.credentials.json` → `claudeAiOauth.accessToken` | `$CODEX_HOME/auth.json` → `tokens.access_token` |
| Path override | `CLAUDE_CONFIG_DIR` | `CODEX_HOME` |

Neither endpoint is documented or promised. Both are read-only, and every failure
path in ACT degrades to *usage unavailable* rather than to a wrong number.

### How they were found

Neither CLI will print usage non-interactively — `claude -p "/usage"` treats it as a
prompt, and there is no `claude usage` subcommand — so both were located by mining
the installed binaries for endpoint strings:

- Claude Code's binary contains `/api/oauth/usage` alongside the other
  `/api/oauth/*` paths.
- Codex's binary contains a path table with `/api/codex/usage`, `/wham/usage`,
  `/api/codex/rate-limit-reset-credits` and `/wham/rate-limit-reset-credits`.
  Only the `/wham/*` pair is actually routed: unauthenticated, `/wham/usage`
  answers **401** while `/api/codex/usage`, `/codex/usage` and a nonsense control
  path all answer **403**. A 401 against a 403 control is what identifies a real
  route without needing a credential.

## Response shapes

### Claude Code

```json
{"five_hour": {"utilization": 16.0, "resets_at": "2026-07-31T07:59:59.088971+00:00"},
 "seven_day": {"utilization": 29.0, "resets_at": "2026-08-01T17:00:00.088993+00:00"},
 "limits": [
   {"kind": "session",       "percent": 16, "severity": "normal", "resets_at": "…", "is_active": false},
   {"kind": "weekly_all",    "percent": 29, "severity": "normal", "resets_at": "…", "is_active": true},
   {"kind": "weekly_scoped", "percent": 0,  "severity": "normal", "resets_at": null,
    "scope": {"model": {"display_name": "Fable"}}}]}
```

**Bind to `limits[]`, not to the named pair.** `kind` names the window explicitly
and `percent` is unambiguously 0–100. The top-level `utilization` is *also* 0–100
here (16.0, 29.0, agreeing with `limits[].percent`), even though the CLI binary
contains `used_percentage: utilization * 100` for its statusline payload — so the
named pair is kept only as a fallback for a response without `limits`.

`weekly_scoped` is a per-model window that reports `resets_at: null`. There is
nothing to count down to, so ACT skips it.

`severity` is carried in the response but its vocabulary beyond `"normal"` has not
been observed, so ACT does not map it. Colour thresholds are ACT's own.

### Codex

```json
{"plan_type": "free",
 "rate_limit": {"allowed": true, "limit_reached": false,
   "primary_window":   {"used_percent": 5, "limit_window_seconds": 2592000,
                        "reset_after_seconds": 2470886, "reset_at": 1787944859},
   "secondary_window": null},
 "rate_limit_reached_type": null}
```

On a **paid** plan `primary_window` is the 5-hour window and `secondary_window` is
the weekly one. On **free** there is a single 30-day window and no secondary — which
is why the indicator names a window by its declared length rather than by a fixed
pair of captions.

⚠️ **The paid response body has not been captured.** The window lengths above are
taken from a real `team`-plan rollout (`window_minutes` 300 and 10080) and the field
names from the measured free response. `CodexUsageDialectTests` carries a
constructed paid fixture flagged as pending; replace it with a measured body when a
paid login is available.

The response also carries `email`, `user_id` and `account_id`. **ACT lifts the
`rate_limit` block and nothing else** — no PII into LiteDB, none into logs.

## Three traps

1. **Window units differ across Codex's own two surfaces.** The endpoint says
   `limit_window_seconds: 2592000`; the rollout JSONL says `window_minutes: 43200`.
   Same 30 days, 60× apart.
2. **Reset encodings differ between vendors.** Anthropic sends ISO-8601 with an
   offset; OpenAI sends epoch **seconds**. Codex additionally sends
   `reset_after_seconds`, which ACT prefers — a server-computed countdown survives a
   local clock that disagrees with the vendor's.
3. **Codex's `auth.json` has no expiry field.** Claude's has a millisecond-epoch
   `expiresAt`; Codex buries expiry in the JWT. ACT does not decode the JWT — it
   issues the call and treats 401 as unavailable, which it must handle anyway.

## Rules ACT holds itself to

- **Read-only, and never refresh.** Each CLI owns its own credential file and
  refreshes it on use. ACT re-reads before each poll and picks up whatever is
  current. It never writes, and never attempts a refresh: OAuth refresh tokens
  rotate and are typically single-use, so racing the CLI's refresh could invalidate
  the user's login. An expired Claude token is detected from `expiresAt` and skipped
  without spending a request.
- **A rolled-over window reports zero, not the last number it saw.** A reading held
  past its own `resets_at` describes a window that no longer exists. ACT polls, so
  nothing ran in between, and zero is the honest answer — rendered subdued, because
  it is inferred from local knowledge only. Usage from another machine, the web, or
  an IDE extension counts against the same quota and is invisible here.
- **The token is never logged, never persisted, and goes nowhere but the vendor.**

## Rejected alternatives

- **`statusLine` hook.** Claude Code's statusline payload does carry
  `rate_limits.{five_hour,seven_day}.{used_percentage,resets_at}` (epoch seconds),
  and ACT already writes the `--settings` file it would be declared in. Rejected:
  it only fires while a session is live, is absent until that session's first API
  response, and is skipped on an untrusted workspace.
- **Scraping `/usage` from a pty.** Same numbers, but a fragile parse, a process
  spawn per refresh, and it hangs on the directory-trust prompt in any directory the
  CLI has not seen (`hasTrustDialogAccepted` in `~/.claude.json`).
- **Deriving from transcripts.** Token counts are there, but the percentage needs a
  denominator ACT cannot know.
- **Codex rollout `rate_limits`.** Codex writes the same block into
  `$CODEX_HOME/sessions/**/rollout-*.jsonl` on `token_count` events, which ACT
  already tails. Rejected as the primary source because it is only as fresh as the
  last session: measured 2026-07-31, the endpoint reported 5% while the newest
  rollout (two days old) still said 0%. Kept here as a documented fallback.
- **`anthropic-ratelimit-unified-*` / `x-codex-*` response headers.** Real, but ACT
  does not make the API calls; only `--debug api` surfaces them, into the pty the
  user is watching.

## Re-testing

1. `GET` each endpoint with a bearer token read from the credential file; expect
   `200` and the shapes above.
2. Confirm `/wham/usage` still answers `401` unauthenticated while a nonsense path
   under `backend-api` answers `403`.
3. On a paid Codex login, capture the real body and replace the pending fixture in
   `CodexUsageDialectTests`.
4. Confirm Claude still returns `limits[]` with `kind` values `session` and
   `weekly_all`.
