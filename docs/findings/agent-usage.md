# Agent usage — where the 5-hour and weekly numbers come from

**Status: resolved.** Both agents expose a live HTTP usage endpoint, and ACT reads
both. Measured 2026-07-31 against `claude-code 2.1.220` and the Codex CLI at
`AppData\Local\OpenAI\Codex\bin\…\codex.exe`.

This supersedes the roadmap's old open question, *"`/usage` scriptability +
reset-time detection"*. It is not scriptable — and it does not need to be.

Companion reading: *Usage indicator* in `../overview.md`, and the usage rows in
`../repository-structure.md`.

---

## The two endpoints

| | Claude Code | Codex |
|---|---|---|
| Endpoint | `https://api.anthropic.com/api/oauth/usage` | `https://chatgpt.com/backend-api/wham/usage` |
| Method | `GET` | `GET` |
| Auth | `Authorization: Bearer <access token>` | `Authorization: Bearer <access token>` |
| Other headers | **none required** | **none required** |
| Credentials | `~/.claude/.credentials.json` → `claudeAiOauth.accessToken` | `$CODEX_HOME/auth.json` → `tokens.access_token` |
| Expiry stated | `expiresAt` **and** `refreshTokenExpiresAt`, both ms epoch | neither — buried in the JWT |
| Path override | `CLAUDE_CONFIG_DIR` | `CODEX_HOME` |

Neither endpoint is documented or promised. Both are read-only, and every failure
path in ACT degrades to *usage unavailable* rather than to a wrong number.

### Overriding either of them

Both are discovered, so **`appsettings.json` ships no `Usage:Agents` section at all** —
an empty placeholder configures nothing and only suggests that the discovered default
needs help. Add the section when discovery is wrong on a machine, or when a vendor moves
a URL:

```json
"Usage": {
  "Agents": {
    "Codex": { "CredentialsPath": "D:\\codex\\auth.json", "Endpoint": "" }
  }
}
```

One agent, one key, or neither — anything absent or blank falls back to what the dialect
discovered. This is deployment plumbing for a broken install, not a preference: what the
*user* chooses (whether an agent is watched at all) lives in the store, on the *Agents*
settings section.

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

`weekly_scoped` is a per-model window ACT does not name, so it is skipped — on its
`kind`, **not** on its null reset. See the next section for why that distinction is
load-bearing.

### `resets_at: null` — a window that has not started

Measured 2026-08-03. **The 5-hour clock starts on the first request of a window**, so
an account that has been idle reports the session window at 0% with **no reset at
all** — Claude's own usage panel shows `5-hour limit 0%` with a reset time beside the
weekly row and nothing beside the 5-hour one. The same body proves the shape is real:
`weekly_scoped` carries `"percent": 0, "resets_at": null` in every response.

The first version required a reset instant per window and skipped any entry without
one, which produced the bug this section exists to record: the top bar showed a Claude
**weekly** meter and no **5-hour** meter, twice, reading as though ACT could not see the
session quota when in fact the quota was simply untouched. The rules now are:

- **`UsageWindow.ResetsAt` is nullable.** Null means *not started*, which is a
  different reading from a countdown of zero (*about to turn over*) — the meter says
  `· not started` rather than a span, and `HasRolledOver` is false because there is no
  boundary to have crossed.
- **A read that succeeded reports both account windows.** Which entries the body
  carries is a property of the account's activity, not of what ACT managed to see, so
  the Claude dialect completes the pair: a window neither `limits[]` nor the named pair
  mentions is reported at 0% with no reset. `limits[]` still wins per window where both
  describe the same one, and the named pair now *fills* what the array omits instead of
  being discarded whole. Only a body where neither surface is recognisable is *nothing*.
- **A window that has not started cannot block the queue.** `UsageBackpressure`
  returns an instant to wait for, so it ignores windows without one — the queue
  launches and the CLI refuses, which is the same choice ACT makes for no reading at
  all.

The observation was verified against the live endpoint: `five_hour.resets_at` came back
as exactly the first-request minute plus five hours, which is why the value is present
whenever ACT is polling during a session and absent when nothing has run.

Codex is left reporting only the windows its plan declares — a free login genuinely has
one 30-day window, and there is no way to tell a paid account's idle weekly window from
a free account's absent one without a measured paid body.

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
   issues the call and treats 401 as unavailable, which it must handle anyway. So
   Claude reports **`Expired`** without spending a request while Codex reaches the same
   state as **`Unauthorized`**; the two read differently in the top bar on purpose,
   because only one of them is a fact ACT actually knows.

## Rules ACT holds itself to

- **One request per agent every 3 minutes, and never faster than one a minute.**
  `Usage:PollSeconds` defaults to **180** and is clamped to **[60, 3600]** — the floor
  is part of the contract, not a sanity check, because neither endpoint is documented
  or promised and nothing ACT ships or lets a user configure may hammer them. A 5-hour
  window moves about a third of a percent a minute, so three-minute granularity loses
  nothing a reader could act on. The gap is measured **from the end of one request to
  the start of the next** (a delay loop, not a `PeriodicTimer`), so a slow endpoint is
  never asked again the moment it answers. A disabled agent is not polled at all, and
  the check is re-read every pass so switching one off stops its traffic immediately.
- **A failure that cost a request backs off; one that cost nothing does not.**
  `UsageBackoff` doubles the wait per consecutive failure up to a **15-minute ceiling**
  — and the ceiling is a floor for a slower poll, so an hourly setting is never
  shortened by failing. It applies to `Unauthorized`, `Unreachable` and `Failed`, the
  three outcomes that reached the network. `NotSignedIn` and `Expired` are decided
  locally without a request, so they stay on the base interval and recover as soon as
  the user signs in. One success clears the count.
- **A rewritten credential file ends the backoff wait early.** The backoff protects the
  vendor's endpoint, not the local file — and a 401 is cured by exactly one thing, a new
  token, which arrives as a rewrite of that file whenever any CLI run refreshes it. So
  while an outcome a new credential can cure is showing (`NotSignedIn`, `Expired`,
  `SignInRequired`, `Unauthorized`), `UsagePump` watches the credential file and a change
  triggers the next probe immediately, resetting the backoff — held to the 60-second
  floor, so the wake can never break the one-a-minute contract. Added 2026-08-10, after a
  Codex token refreshed by a task launched *through ACT* sat unread for 10+ minutes
  because the pump was asleep at the 15-minute ceiling. `UsageWake` (`Act.Core/Rules`)
  names the outcomes and the floor; `IFileWatcher` (`Act.Infrastructure/FileSystem`)
  is the watch itself, and a directory that does not exist yet simply leaves the plain
  wait in place.
- **Unavailable is displayed, not swallowed.** Every failure path resolves to a
  `UsageAvailability` — `NotSignedIn`, `Expired`, `Unauthorized`, `Unreachable`,
  `Failed` — and the top bar renders an *unavailable* chip in place of that agent's
  meters, naming the cause in one line with the full sentence and the last-checked time
  in the tooltip. Dropping the bar silently, which is what the first version did, left
  no way to tell "you have used nothing" from "ACT has been locked out since this
  morning". `Off` (switched off in configuration) is the one outcome that shows
  nothing, because the user asked for nothing.
- **Read-only, and ACT never performs the refresh itself.** Each CLI owns its own
  credential file and refreshes it on use. ACT re-reads before each poll and picks up
  whatever is current. It never writes a credential and never performs the OAuth
  exchange: refresh tokens rotate and are typically single-use, so racing the CLI's
  own refresh could invalidate the user's login. An expired Claude token is detected
  from `expiresAt` and skipped without spending a request — and then *nudged*, which
  is the next section.
- **A rolled-over window reports zero, not the last number it saw.** A reading held
  past its own `resets_at` describes a window that no longer exists. ACT polls, so
  nothing ran in between, and zero is the honest answer — rendered subdued, because
  it is inferred from local knowledge only. Usage from another machine, the web, or
  an IDE extension counts against the same quota and is invisible here.
- **The token is never logged, never persisted, and goes nowhere but the vendor.**

## The expiry nudge — asking the CLI to refresh its own token

Measured 2026-08-04 against **claude-code 2.1.220**.

`Expired` used to be a dead end: the meter went dark and stayed dark until the user
happened to run the CLI again. That is a real state to be in — a subscription user who
works in the *desktop app* rather than the terminal can leave `~/.claude/.credentials.json`
untouched for weeks — and it reads as though ACT had been locked out when in fact the
login is perfectly good and merely stale.

**The file states two clocks, and ACT reads both.** Measured on a real `team` login:

| Field | Lifetime | Measured |
|---|---|---|
| `expiresAt` | **8 h** | issued 06:37:49, expires 14:37:49 the same day |
| `refreshTokenExpiresAt` | **~21 days** | 21.11 d from issue |

So an expired access token beside a live refresh token is the normal resting state of an
install nobody has used today, and the fix is not a new credential — it is to make the CLI
notice. Every Claude Code frontend shares that one file and refreshes it on use, which
is why the measured file on this machine carried a token minted hours earlier despite
the *terminal* CLI not having been run in weeks.

The second clock is what makes the difference between two states ACT used to conflate,
and both are read locally without spending a request:

- **`Expired`** — access token stale, refresh token still good. Recoverable *without the
  user*: nudge the CLI and the numbers come back. The chip says `renewing token`.
- **`SignInRequired`** — both clocks run out, which takes about three weeks of touching
  no Claude Code frontend at all. No nudge is attempted, because ACT knows for a fact
  that none can help; only `claude auth login` will. The chip says `sign in again`.

Rendering both as `token expired` was the earlier bug: the second case sends the user
looking for a fault in ACT when what they need is a login.

⚠️ **The order of the two checks is load-bearing.** The refresh clock is read *only* when
the access token has already expired. The request ACT is about to make uses the access
token, so a refresh token that lapsed while the access token is still valid is not a
problem yet — checking it first would blank a working meter for the final hours of a
login that merely happens to be near its end. A test pins this
(`A_lapsed_refresh_token_does_not_withhold_an_access_token_that_still_works`).

`refreshTokenExpiresAt` absent — a CLI old enough not to write it — reads as *no reason to
think it has lapsed*, which keeps the free nudge in play and lets the truth arrive on the
next poll. `rateLimitTier` (`default_claude_max_5x` on the measured file) is also present
and deliberately unread: it names a plan, and every number ACT shows comes from the
endpoint instead.

So when the probe answers `Expired`, `UsagePump` runs the **`-p` query path** —
the same measured command line `agent-title.md` documents, reached through
`IAgentAdapter.QueryAsync` so the flag set keeps exactly one owner — and, if it answers,
**re-reads the credential file and probes once more** in the same pass. `UsageRefresh`
(in `Act.Core/Rules`) owns the decisions, and all of them are pure: which availability a
nudge answers, whether the cooldown has passed, and how far a wasted nudge lengthens it.

### `auth status` was measured and rejected

**Measured 2026-08-04 against a genuinely expired access token** (the honest test: the
real install, its own file, a refresh token still valid for 20 days — no copied
credentials, so nothing could consume the user's refresh token):

| Command | Elapsed | `expiresAt` after | File rewritten |
|---|---|---|---|
| `claude auth status --json` | 466 ms | **unchanged** | no |
| `claude -p --safe-mode … --tools ""` | 4,042 ms | **moved forward 8 h** | yes |

`auth status` **reads** the credential file; it does not refresh it. It exits 0 and
reports `loggedIn: true` while leaving the stale token exactly where it was — so a nudge
built on it logs success and fixes nothing. That was this feature's first shape, and the
live symptom was a chip stuck on `renewing token` with the probe logging
`access token has expired` every three minutes.

The 4 s versus 466 ms gap is the tell: only one of them makes a network round trip.

**So the nudge is not free**, and the cost is the whole design constraint:

- **~5,000 tokens per refresh** (≈ $0.008–0.010), per the cost table in
  `agent-title.md`. In steady state that is *one* nudge per 8-hour token
  lifetime — a few cents a month — because a successful refresh buys 8 hours of
  `Available`.
- **`Usage:RefreshOnExpiry`** (default `true`) switches it off entirely for anyone who
  would rather the meter stay dark than spend a token on it.
- **`Usage:RefreshCooldownSeconds`** defaults to **900** (15 min), clamped to
  **[60, 86400]** and tracked per agent, and it **doubles after every nudge that left the
  token expired**, up to a 2-hour ceiling. That escalation is the important half: the
  realistic failure is a refresh token revoked server-side while its stated expiry is
  still in the future, which never heals, and a fixed cooldown would spend 5,000 tokens
  against it four times an hour forever. `UsageBackoff` throttles requests to someone
  else's endpoint; `UsageRefresh` throttles *spending*.

Two properties survive from the first shape unchanged:

- **ACT still performs no OAuth exchange and writes no credential.** The CLI does both,
  on its own file, as its single owner. What changed is that ACT asks rather than waiting
  to be asked.
- **A nudge that could not be delivered does not spend a request.** The re-probe happens
  only when the command actually answered; a missing or broken CLI changed nothing, and
  the endpoint is undocumented enough that a request which cannot tell ACT anything new
  should not be made.

**Still untested, and worth trying at the next natural expiry:** `claude doctor` makes no
model call and might touch auth on the way to reporting install health. If it refreshes,
the nudge becomes free again and the cooldown reverts to a courtesy rather than a budget.
It could not be measured here because the decisive test above had just refreshed the token.

**Claude Code only, and that asymmetry is the point.** `Expired` is a fact ACT reads out
of `expiresAt` locally. Codex states neither expiry (trap 3 above), so it reaches the same
situation as `Unauthorized` — which is indistinguishable from a revoked login, and
spawning a process on the guess would be a shot in the dark that also costs a request.
`NotSignedIn` and `SignInRequired` are excluded for the opposite reason: both need
`claude auth login`, which needs a terminal ACT has not got.

### Refresh tokens really do rotate — and the window does not slide

Measured across the refresh above: `refreshTokenExpiresAt` went `1787664070979` →
`1787664070697`. A **new refresh token was issued**, which settles the assumption the
"never refresh" rule was written on — and retroactively justifies refusing to run the
forced-expiry test against a copied credential file, since that would have consumed the
real install's refresh token and could have logged the user out.

The **deadline barely moved** (282 ms, against the ~9.7 hours it would have shifted if the
window restarted). So the ~21-day refresh window is anchored to the *original login* and
does **not** extend on use. A user who never signs in again will hit `SignInRequired`
about three weeks after their last real `claude auth login`, however actively they use
ACT in between — which is exactly why that state earns its own chip and its own message
rather than being folded into `Expired`.

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
- **Borrowing the Claude *desktop app's* token when the CLI's has expired.** The
  tempting fallback: the desktop app is signed in, so read its token and retry. Rejected
  on three counts, and the third is the one that settles it. (1) It is a *different OAuth
  client*, so its token may carry neither the audience nor the scope
  `/api/oauth/usage` wants, and a 401 is the likeliest outcome. (2) It is not a plaintext
  JSON file: an Electron app keeps a secret in the OS credential store via `safeStorage`,
  so reading it means DPAPI decryption and a Windows-only code path — and code that hunts
  through another application's credential store is indistinguishable from credential
  theft to any reader, EDR included. (3) **It is unnecessary.** Every Claude Code frontend
  shares `~/.claude/.credentials.json`, and the refresh token in it outlives the 8-hour
  access token by a wide margin, so the login ACT already has is not stale — it is merely
  unrefreshed. The nudge above fixes it for free and stays inside the one-owner rule.

## Re-testing

1. `GET` each endpoint with a bearer token read from the credential file; expect
   `200` and the shapes above.
2. Confirm `/wham/usage` still answers `401` unauthenticated while a nonsense path
   under `backend-api` answers `403`.
3. On a paid Codex login, capture the real body and replace the pending fixture in
   `CodexUsageDialectTests`.
4. Confirm Claude still returns `limits[]` with `kind` values `session` and
   `weekly_all`.
5. After five idle hours, confirm the session window still comes back with
   `resets_at: null` rather than disappearing from `limits[]` — and that the top bar
   draws it as `0% · not started` either way.
6. **Re-confirm the nudge still refreshes.** With an access token that has genuinely
   expired — wait for it rather than copying the credential file, since a refresh
   consumes the rotating refresh token — note `claudeAiOauth.expiresAt`, let ACT nudge
   (or run the `-p` line by hand), and confirm `expiresAt` moves forward. A CLI version
   that stops refreshing on a print-mode run turns the whole feature into a silent
   token spend.
7. **Retry the free candidates.** At the same expiry, before letting the `-p` nudge run:
   does `claude doctor` move `expiresAt`? (`claude auth status --json` was measured and
   does not — see the table above.) Anything that refreshes without a model call makes
   the nudge free and lets the cooldown go back to being a courtesy.
