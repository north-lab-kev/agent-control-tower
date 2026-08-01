# Codex hooks — findings, and how to re-test the blind implementation

**Status as of 2026-07-31: hooks fire, and ACT's ingestion through them works.** Verified
live on `codex-cli 0.146.0-alpha.3.1` (card #1091): `SessionStarted`, `ActivityObserved` ×3
and `TurnEnded` normalized from real payloads, plus `PermissionRequested` (`apply_patch`)
raising a `needs permission` badge on card #1092 from a prompt ACT never touched.

**The bug was ACT's, and it was one character class: quotes.** Codex takes quotes literally
when it resolves the program, so the command ACT had emitted since its first launch —
`"C:\…\act-hook-forward.cmd" SessionStart` — named a program called `"C:\…"`, which does not
exist. Every hook died with `hook exited with code 1` and the script was never reached. The
rule, measured rather than guessed: **the program token must be unquoted; everything after it
may be quoted**, because from there `cmd` is doing the parsing. See *The exec failure*.

That also reverses two conclusions this file used to carry: the 2026-07-29 status
(*"discovered and parsed, never executed"* — they execute) and the claim that Codex can never
report a waiting prompt (it can, via `PermissionRequest`). The upstream issue
<https://github.com/openai/codex/issues/17532> is no longer what stands in the way.

Payloads carry `session_id` **and `transcript_path`**, so `CodexRolloutFinder` — written to
stand in for a hook that never fired — now has a real replacement available; retiring it is
tracked on the roadmap, not done.

Companion reading: the *Codex facts* and *Codex hook findings* sections of
`ACT-roadmap.md`, and *Local-endpoint security* in `ACT-overview.md`.

---

## What is established (verified, not assumed)

### Hooks live in a JSON file — not in `config.toml` tables

The roadmap originally said ACT would write `[[hooks.*]]` TOML tables into a
`--profile` layer. That is wrong.

- `hooks` in `config.toml` is a **string** holding an absolute path to a JSON file:
  `hooks = "C:\\path\\to\\hooks.json"`. Writing a `[[hooks.SessionStart]]` table fails
  with `Error loading config.toml: invalid type: sequence, expected a string in `hooks``.
- **`$CODEX_HOME/hooks.json` is auto-discovered** with no config key at all. Proven by
  corrupting the file: every session start then prints
  `warning: failed to parse hooks config <path>: …`. That warning is the cheapest
  available probe that the file is being read.
- The file **must be BOM-free**. PowerShell's `Out-File -Encoding utf8` writes a BOM and
  the parser dies with `expected value at line 1 column 1`. Use
  `UTF8Encoding($false)` / .NET defaults without BOM.

Schema (as accepted by the strict serde parser — unknown fields are rejected, which is
how it was mapped):

```json
{
  "description": "optional",
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup",
        "hooks": [
          { "type": "command", "command": "<single string, not an array>" }
        ]
      }
    ]
  }
}
```

- Top level accepts only `description` and `hooks` (`unknown field 'SessionStart',
  expected 'description' or 'hooks'`).
- `hooks` is a map of **event name → array of matcher groups**; each group has `matcher`
  and its own `hooks` array of handlers.
- `command` is a **string**. An array (`["sh","-c","…"]`, as some docs show) fails with
  `invalid type: sequence, expected a string`.
- `SessionStart` matchers are documented as `startup` / `resume` / `clear`; `"*"` was
  also accepted without complaint.
- Only `type: "command"` handlers run. `prompt` / `agent` handler types parse and are
  skipped.

### The exec failure, and the rule that fixes it — measured 2026-07-31

**A quoted program token is never executable.** Codex does not strip quotes when resolving
the program, so `"C:\path\x.cmd" SessionStart` asks for a program literally named
`"C:\path\x.cmd"`. Result: `hook exited with code 1`, the script never reached.

Two rounds of measurement, each costing exactly one trust prompt by giving **five events five
different candidate shapes at once** — the review screen says *"5 hooks are new or changed"*
and one answer trusts them all. Round one:

| Command shape | Ran? |
|---|---|
| `<path>\pexe.exe` — real exe, no args | ✅ |
| `<path>\pexe.exe UserPromptSubmit` — real exe, with arg | ✅ |
| `<path>\p1.cmd` — script, unquoted | ✅ |
| `"<path>\p2.cmd"` — script, **quoted** | ❌ |
| `cmd.exe /c <path>\p3.cmd` — via shell, all unquoted | ✅ |

So it is not the `.cmd` extension, not arguments, and not the shell — only the quoting.
Round two asked whether a quoted argument is safe, since ACT's forwarder lives under
`%LOCALAPPDATA%` and a username with a space puts a space in the path:

| Command shape | Ran? |
|---|---|
| `cmd.exe /c <no-space>\p1.cmd SessionStart` | ✅ |
| `cmd.exe /c "<path with space>\p2.cmd" UserPromptSubmit` | ✅ |
| `cmd.exe /c "<no-space>\p3.cmd" PreToolUse` | ✅ |

**The shipped shape is therefore `<cmd.exe unquoted> /c "<forwarder>" <Event>`** — unquoted
program so Codex can exec it, quoted path so `cmd` handles spaces. Pinned by two tests in
`CodexHookTests`.

Ruled out along the way, so nobody re-checks them:

- **Not the sandbox.** `~/.codex/.sandbox/sandbox.<date>.log` records a `START:` line for
  every sandboxed command, and it showed only the session's own tool calls — never a hook.
- **Not the script's own exit code.** Run by hand, with and without
  `ACT_HOOK_ENDPOINT` / `ACT_HOOK_TOKEN` set, through `cmd`, `sh -c`, direct exec and
  PowerShell, the forwarder exits 0 on every path.
- **Not the dialect.** Swapping the forwarder's contents for a POSIX-sh script (the hash
  covers the command string, not the file, so this needs no re-trust) changed nothing.

**The technique worth reusing:** Codex prints no detail beyond the exit code,
`~/.codex/logs_2.sqlite` carries no hook records and the rollout `.jsonl` has none either —
so there is nothing to read, only experiments to run. Distinct probe files that each log their
own name, one per event, turn a yes/no into a table for the price of one trust prompt.

### Trust is written into ACT's own profile, and ACT used to destroy it

The 2026-07-29 note below says trust lands in the user's `~/.codex/config.toml`. **It does
not.** Measured 2026-07-31: Codex writes `[hooks.state.…]` into the *file the hook was
declared in* — which for ACT is `~/.codex/act.config.toml`, ACT's own generated profile,
whose header reads *"Overwritten on every launch"*. So ACT threw away the review the user
had just answered and the nine-hook gate returned every launch.

Fixed by `IAgentConfigFiles.WriteExternalPreservingTail`, which carries everything from the
first `[hooks.state` line across verbatim. **Comparing the file against ACT's own bytes is
not enough** — that was the first attempt and it failed live, because Codex rewrites the
whole file in its own formatting when it saves, so ACT's block does not come back
byte-identical. Verified live: a relaunch reaches the TUI with no gate.

### Trust is a blocking, pre-session gate

A new or changed hook definition produces a full-screen TUI prompt **before the session
starts**:

```
Hooks need review
2 hooks are new or changed.
Hooks can run outside the sandbox after you trust them.

 1. Review hooks
 2. Trust all and continue
 3. Continue without trusting (hooks won't run)
```

Consequences for ACT: a Codex card's first launch parks on this screen and goes nowhere
until the user answers it *in the terminal* — which fits ACT's model (the user answers
everything in the terminal), but the card must not read it as a hang. It is a **second**
pre-session gate, stacked on the directory-trust prompt.

Trust is persisted keyed by file path, event, group index and handler index, with a sha256
of the definition. ⚠️ The path below is **wrong** — see *Trust is written into ACT's own
profile* above; it goes into the file that declared the hook, not the user's `config.toml`:

```toml
[hooks.state]

[hooks.state.'C:\Users\<u>\.codex\hooks.json:session_start:0:0']
trusted_hash = "sha256:8325e47cc522a7a08a31dae53179314d8e0996dd4443b46d438e4a2bc567a3bd"
```

So ACT cannot keep the user's `config.toml` pristine — *Codex* writes to it, not ACT.
And the per-handler hash confirms the **byte-stability rule**: anything that varies per
launch inside a hook definition re-triggers the review screen every launch. Hence the
per-session token and the endpoint URL both ride the **process environment**, never the
command string.

`HookTrustStatus` values seen in the binary: `Managed | Untrusted | Trusted | Modified`
(`New hook - review required`, `Modified since last trusted - review required`).
Managed hooks (system/MDM/`requirements.toml`) are trusted by policy.

### Escape hatch

`--dangerously-bypass-hook-trust`, or `-c bypass_hook_trust=true` — *"Enabled hooks may
run without review for this invocation."* Skips the review screen. Useful for testing;
**ACT must not ship it on** (it would silently opt the user out of a security gate).

### …and yet nothing ever executed

Discovered and parsed: yes. Executed: never. Everything below was tried:

| Variable | Values tried |
|---|---|
| Event | `SessionStart`, `UserPromptSubmit` |
| `matcher` | `"*"`, `"startup"` |
| Trust | untrusted; trusted via the review screen; `bypass_hook_trust=true` |
| Mode | `codex exec` (several runs, incl. a full successful turn); interactive TUI in a real console with a real tty |
| Config location | `$CODEX_HOME/hooks.json`; `-c hooks.…` overrides; a `--profile` layer; `codex_hooks.…` key |
| Working directory | fresh temp dir; a directory already `trust_level = "trusted"` |
| Hook command | `cmd /c echo … >> file`; and finally a **single-token `.bat` path** with no args, quoting, or redirection, so shell parsing could not be at fault |

No output file was ever written, and the session rollout `.jsonl` contains no hook
entries. `codex exec` additionally appears to skip hooks entirely — even trust-bypassed.

**The original question ACT needed answered is therefore still open:** whether a
`command` hook interpolates `$VAR` itself, or execs without a shell so the command must
be `cmd /c …` to read the inherited environment. Nothing ran, so nothing could be
measured. ACT's implementation hedges (below).

---

## How ACT implements it, blind

Shipped in `Act.Agents.Codex/CodexHookConfig.cs` (config generation) and
`CodexHookNormalizer.cs` (payload → ACT events), wired from `CodexAdapter.InjectHooks`.
Both files carry a **PENDING — WRITTEN BLIND** header. Tests in
`tests/Act.Agents.Tests/CodexHookTests.cs` pin what ACT *writes*, which is all that can be
checked while nothing fires — **a green suite there is not evidence that ingestion works.**

Design choices and *why*, so a later session can tell a deliberate decision from a bug:

- **Write `hooks.json` into ACT's own data directory and point at it** with
  `hooks = "<abs path>"` from the `--profile` layer (`$CODEX_HOME/act.config.toml`), rather
  than dropping a file into `$CODEX_HOME/hooks.json`. Auto-discovery works, but that path is
  the *user's* file — ACT must never own or overwrite it. The profile file is the one thing
  ACT writes outside its own directory, because Codex reads profile layers only from there.
- **One hook command, identical for every session, every launch and every card**, so the
  trust hash is stable and the review screen appears at most once:

      "<act-data>/agent-config/act-hook-forward.cmd" <EventName>

  Everything variable — endpoint url, per-session token, task id — is supplied through the
  PTY process environment (`ACT_HOOK_ENDPOINT`, `ACT_HOOK_TOKEN`, `ACT_TASK_ID`). Note the
  forwarder and the hooks json are **shared, not per task**: the forwarder's path appears
  inside the hashed definition, so a per-task path would mean a fresh trust prompt for every
  card the user ever creates. There is a test for exactly that
  (`The_hook_definition_is_byte_identical_across_launches_and_carries_no_secret`) — it caught
  the mistake when the paths *were* per task.
- **A script, not an inline `curl`.** It is the hedge for the unanswered expansion question:
  read inside a shell, `$VAR` / `%VAR%` resolve from the inherited environment whether or not
  Codex interpolates first. It also means the definition never changes when the endpoint does.
- **Observability only.** The forwarder always exits `0`, never emits
  `permissionDecision: "deny"` / `decision: "block"`, never exit code 2. A hook that can
  block is a hook that can wedge the user's session, and ACT decides nothing.
- **Never `bypass_hook_trust`.** The review screen is the user's call.
- **Files and process signals are the primary path, not the fallback.** Session binding
  comes from the rollout file (`~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`,
  indexed by `~/.codex/session_index.jsonl`), because `SessionStart` — the intended source
  of `session_id` — does not arrive. Hooks are an *upgrade* that switches on if and when
  they fire.

---

## Re-test checklist (for a future session)

Run this against a newer `codex-cli` before trusting any of the above.

1. **Find a runnable CLI.** The Store-packaged `codex.exe` under `C:\Program Files\
   WindowsApps\OpenAI.Codex_*\app\resources\` is readable but **cannot be executed** by a
   normal process (access denied), and `codex` is **not on `PATH`** on a desktop-app-only
   install. The runnable copy is the `CODEX_CLI_PATH` recorded in
   `~/.codex/config.toml` — `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`.
   *(This also matters for `ExecutableResolver`: resolving `codex` by name fails on such
   an install.)*
2. **Confirm the version moved** and check whether the issue above shipped:
   `codex --version`.
3. **Smoke-test discovery.** Write deliberately invalid JSON to `$CODEX_HOME/hooks.json`
   and start a session; expect `warning: failed to parse hooks config …`. If that warning
   is absent, discovery itself changed — re-map the schema before anything else.
4. **Smoke-test execution with the simplest possible hook.** BOM-free file, a
   `SessionStart` / `matcher: "startup"` handler whose `command` is a single-token
   absolute `.bat` path that appends one line to a file, plus
   `-c bypass_hook_trust=true` so no keypress is needed:

   ```json
   { "hooks": { "SessionStart": [ { "matcher": "startup",
       "hooks": [ { "type": "command", "command": "C:\\tmp\\probe.bat" } ] } ] } }
   ```

   Start the **interactive TUI** (not `exec` — it appears not to run hooks) in a real
   console. If the file appears, hooks are back.
5. **Then answer the deferred question:** give the probe arguments
   `probe.bat $ACT_HOOK_ENDPOINT %ACT_HOOK_ENDPOINT%` and have it log `%1`, `%2` and
   `%ACT_HOOK_ENDPOINT%`. That distinguishes *Codex interpolates `$VAR`* from *the child
   inherits the environment and a shell expands it* — and tells us whether the
   shell-script hedge can be dropped for a direct `curl`.
   *(Contrast with Claude Code, where this is settled: `--settings` accepts
   `type: "http"` hooks with a custom header, verified live, so no forwarder is involved.)*
6. **Verify the payload.** Confirm each event carries `session_id`, `cwd`,
   `hook_event_name`, `model`, `permission_mode`, `turn_id`, `transcript_path`, and that
   `PermissionRequest` fires for a real approval prompt. If so, Codex regains the
   event-based permission signal ACT currently lacks for both agents.
7. **Confirm the trust story end to end:** that ACT's stable command template triggers the
   review screen exactly once, and that relaunching (and restarting ACT) does not
   re-trigger it. That is the whole reason the URL and token live in the environment.

### Other tripwires hit while testing, worth not re-discovering

- `codex debug models` no longer matches the roadmap's effort table (`gpt-5.6-sol` is
  listed now), and the ChatGPT-account entitlement is narrower than the catalog:
  `gpt-5.4`, `gpt-5.6-sol` and `gpt-5.4-mini` were all rejected with *"not supported when
  using Codex with a ChatGPT account"*; `gpt-5.5` worked. Resolve models at runtime rather
  than pinning a table.
- A zero-token probe: `-m <unsupported-model> exec …` initialises the session (so config
  and hooks load, and warnings print) and then fails at the model request without spending
  anything. Good for schema iteration; **not** good for testing hook *execution*, since it
  aborts early.
- There is an app-server JSON-RPC method **`hooks/list`** (observed in
  `~/.codex/logs_2.sqlite`) which would be the clean, non-interactive way to inspect hook
  registration and trust status. The `initialize` handshake shape was not worked out —
  `{"method":"initialize","params":{"clientInfo":{…}}}` drew no response and a subsequent
  call returned `{"error":{"code":-32600,"message":"Not initialized"}}`. Worth another
  look; it would replace most of the guesswork above.
