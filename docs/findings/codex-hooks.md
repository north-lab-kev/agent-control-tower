# Codex hooks — how they actually work, and the wrong turns taken to find out

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
stand in for a hook that never fired — **has been deleted**, along with `ITranscriptFinder`
and `ITranscriptDirectory`. Both agents now learn where their transcript is the same way: the
hooks say so. The accepted cost: answering the hook-review screen with *"Continue without
trusting"* leaves a Codex card with no payloads, so nothing names its rollout and it reports
only what its process can say.

Companion reading: the *Codex facts* and *Codex hook findings* sections of
`../roadmap.md`, and *Local-endpoint security* in `../overview.md`.

---

## What is established (verified, not assumed)

### Where hooks are declared: `[[hooks.*]]` tables in ACT's `--profile` layer

**ACT writes TOML tables, and this is the shape that works** — measured 2026-07-31 and
pinned by `CodexHookTests`:

```toml
[[hooks.SessionStart]]
matcher = "*"

[[hooks.SessionStart.hooks]]
type = "command"
command = "C:\\WINDOWS\\system32\\cmd.exe /c \"C:\\…\\act-hook-forward.cmd\" SessionStart"
```

> ⚠️ **This section said the exact opposite until 2026-07-31**, under a heading claiming it was
> verified: that `hooks` is a *string* path to a json file and that a `[[hooks.*]]` table is
> rejected. On this CLI it is the string form that is rejected — *"invalid type: string …
> expected struct HooksToml"* — and every Codex launch died on exit code 1 with an empty
> terminal until the shape was re-measured. Same version string, opposite result, so an alpha
> rebuild moved it under the original measurement. **Type-probe the parser before trusting any
> claim in this file** (see the trap below); the two shapes are one keystroke apart in effect
> and total in consequence.

`$CODEX_HOME/hooks.json` **is** auto-discovered with no config key, so a json file is a real
second way in — but not one ACT uses: that path is the user's file, and ACT will not write it.
ACT briefly wrote the same json (`act-hooks.json`) into its own directory, where Codex never looks; that file is
deleted. Two facts from mapping it are still worth keeping:

- A hooks json **must be BOM-free**. PowerShell's `Out-File -Encoding utf8` writes a BOM and
  the parser dies with `expected value at line 1 column 1`. Use `UTF8Encoding($false)` /
  .NET defaults without BOM. (ACT's `AgentConfigFiles` does, for every file it writes.)
- The **handler shape is the same in both dialects**, which is why the json schema below still
  describes what the profile declares.

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

### `needs answer` — verified live 2026-07-31, by typing `/plan` in ACT's own terminal

Codex's ask-the-user tool is **`request_user_input`**, named in the base instructions the rollout
records: *"Use the `request_user_input` tool only when it is listed in the available tools for this
turn … In Default mode, strongly prefer making reasonable assumptions."*

`CodexHookNormalizer` keys `PreToolUse` on that tool name and raises `QuestionAsked`, exactly as
`ClaudeCodeHookNormalizer` does for `AskUserQuestion`. Without it a question would normalize to
plain activity, so a card blocked on one would sit in Executing reporting `running`.

✅ **Verified live on card #1097**, using the one path that is actually reachable: `/plan` typed into
ACT's embedded terminal, then an ambiguous request. Codex called `request_user_input`, the payload
normalized to `QuestionAsked` — *"What filename should the greeting file use? · What language/content
style should the greeting use?"* — and the card badged **`needs answer`**. The ` · ` join shows the
text came from the **`questions` array** branch, each entry carrying a `question` field, so that is
the real shape; `prompt` and `question` remain as fallbacks.

Measured 2026-07-31 about how it is reached:

- Asked to call it in Default mode, the CLI replies *"I can't use `request_user_input` in the current
  Default mode"*, asks the question in prose, and ends the turn — so the card lands on `to review`,
  which is honest rather than wrong.
- **Plan mode is a TUI slash command, `/plan` — not a flag and not a config key.** `--help` offers no
  mode option; `collaboration_mode`, `mode` and `collaboration` were each given the wrong type and none
  produced a serde error, which for a parser that *ignores unknown keys* means none of them exist. The
  strings in `codex.exe` confirm the shape: `/plan`, *"Continue planning with the model"*,
  `<proposed_plan>`, and *"If you try to use `update_plan` … in Plan mode, it will return an error"*.
- ACT's `PermissionMode.Plan` is a different axis entirely — it resolves to
  `--ask-for-approval never --sandbox read-only`, which does not change the collaboration mode.

**Decided 2026-07-31: ACT adds nothing for this.** Not a Codex-only collaboration-mode field (there is
nothing to set — `/plan` is typed into the TUI, and ACT does not drive the TUI), and certainly not Plan
mode by default: Plan mode *proposes* work instead of doing it, which would make every Codex card a
plan and defeat the unattended queue Phase 5 is built for. The classification stays because it is cheap
and **not** dead code — a user can type `/plan` in ACT's embedded terminal themselves, and the card
then badges correctly. And the badge is the weaker half of the argument anyway: a prose question ends
the turn, so the card is already in Your turn where the user will see it. `needs answer` versus
`to review` differs in the *reason*, not the action — the same reasoning that merged "Needs feedback"
into "To review".

### A blocked card could flip back to `running` — found and fixed on the same run

On card #1097 the badge read **`running` while a Bash approval was still on screen**. The arrival order
was `PermissionRequested (Bash)` → `ActivityObserved`, and the activity moved the card out of Your turn
before anyone had answered anything.

The cause is that **`UserPromptSubmit` and the tool events normalize to the same event**,
`ActivityObserved`. Step 9's design says observed activity is "the one way out of Your turn" and means
`UserPromptSubmit` by it — but the rules engine cannot tell the two apart, so a `PreToolUse` or
`PostToolUse` arriving after a permission request clears the badge just as a user's keystroke would.

Two candidate mechanisms, not yet separated:

- Codex emits `PostToolUse` for the *previous* tool after the `PermissionRequest` for the next one.
- Or the posts simply race: the forwarder spawns one `curl` per hook, so **ACT sees arrival order, not
  emission order**, and two hooks firing milliseconds apart can invert.

**Fixed** in `RulesEngine`: a tool-bearing `ActivityObserved` no longer clears a `needs permission`
card. `ActivityObserved.ToolName` is null exactly for `UserPromptSubmit`, so no new signal was needed.

**The cost was chosen, not overlooked.** Approving a prompt in the terminal fires no
`UserPromptSubmit`, so an approved card keeps saying `needs permission` until the turn ends. That is
the better of the two wrongs: a stale "you are needed" costs a glance, a stale `running` hides a
session waiting on a human — the one thing the board exists to prevent.

**And the guard covers permissions only.** A question is raised by its tool's `PreToolUse` and
*answered* at its `PostToolUse`, so for `needs answer` the tool event really is the user acting —
Claude Code's `AskUserQuestion` recovery depends on it, and an existing test caught the over-broad
first attempt. A permission has no paired event reporting the approval, which is exactly why only that
half needs guarding.

#### Considered and rejected: watching the PTY for the user's Enter

ACT does see the keystrokes the user types into xterm, so treating an Enter as "the prompt was
answered" is technically available. It should not be done:

- **Enter cannot say what was answered.** Arrow-down + Enter picks *"No, and tell Codex what to do
  differently"* — a denial, after which the card is still the user's. Approve and deny would be one
  signal.
- **Enter is not specific to the prompt.** It submits composer text, dismisses notices, and inserts
  newlines. An Enter aimed at anything else would clear a real block.
- **It is the guess this repo has already deleted once.** Step 7 shipped a readiness heuristic (first
  output = painted, 300 ms quiet = ready), it silently swallowed every opening prompt, and the fix was
  *deleting the guess* — recorded there as "no better guess exists, because knowing when the prompt
  line is live means reading the screen, which ACT does not do". Inferring an answer from a keypress is
  the same move.
- **The payoff is a few seconds.** The next real signal — the approved tool's own event, or `Stop` —
  arrives on its own shortly after.

The non-guessing version of the same idea, if the stale badge ever becomes worth removing:
**correlate the resolving event by id.** `PermissionRequested` already carries a `RequestId`, and Codex
payloads carry a `tool_call_id`; if the approved tool's `PostToolUse` reports the same call, that is
proof the block ended rather than an inference — and being id-matched it is immune to the arrival-order
race as well. It needs one measurement first: whether `PostToolUse` and `PermissionRequest` agree on an
id for the same call.

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

---

## How ACT implements it

`Act.Agents.Codex/CodexHookConfig.cs` generates the config, `CodexHookNormalizer.cs` folds
payloads into ACT's events, and `CodexAdapter.InjectHooks` wires both. Two files are written
per launch — the forwarder and the profile — and `CodexHookTests` pins their shape.

- **Declared in ACT's own `--profile` layer** (`$CODEX_HOME/act.config.toml`), never in the
  user's `config.toml` and never in `$CODEX_HOME/hooks.json`. The profile is the one thing ACT
  writes outside its own directory, because Codex reads profile layers only from there — and it
  is written **preserving any `[hooks.state]` tail**, because Codex appends its trust hashes to
  that same file.
- **One hook command, identical for every session, every launch and every card**, so the
  trust hash is stable and the review screen appears at most once per definition change:

      <system32>\cmd.exe /c "<act-data>\agent-config\act-hook-forward.cmd" <EventName>

  Everything variable — endpoint url, per-session token, task id — is supplied through the
  PTY process environment (`ACT_HOOK_ENDPOINT`, `ACT_HOOK_TOKEN`, `ACT_TASK_ID`). The
  forwarder is **shared, not per task**: its path is inside the hashed definition, so a
  per-task path would mean a fresh trust prompt for every card the user ever creates. There is
  a test for exactly that (`The_hook_definition_is_byte_identical_across_launches_and_carries_no_secret`)
  — it caught the mistake when the paths *were* per task.
- **Unquoted program, quoted path.** The single most expensive fact in this file; see *The exec
  failure* above.
- **A script, not an inline `curl`.** Inside a shell the environment variables resolve however
  Codex passes them, and the definition never changes when the endpoint does.
- **Observability only.** The forwarder always exits `0`, never emits
  `permissionDecision: "deny"` / `decision: "block"`, never exit code 2. A hook that can
  block is a hook that can wedge the user's session, and ACT decides nothing.
- **Never `bypass_hook_trust`.** The review screen is the user's call. (`-c
  bypass_hook_trust=true` also proved useless for testing: with it set, hooks did not run at
  all in `exec` mode.)
- **Hooks are the primary path.** They carry `session_id` and `transcript_path`, so they own
  binding, liveness and the turn/tool counts; the rollout file is read for enrichment only.

---

## History: the two months this file spent being wrong

Kept because the *methods* are reusable and because it shows how confidently a wrong
measurement can be written down. Nothing below is current guidance.

**2026-07-29 — "hooks do not fire at all."** Discovered and parsed, never executed. Tried:

| Variable | Values tried |
|---|---|
| Event | `SessionStart`, `UserPromptSubmit` |
| `matcher` | `"*"`, `"startup"` |
| Trust | untrusted; trusted via the review screen; `bypass_hook_trust=true` |
| Mode | `codex exec`; interactive TUI in a real console with a real tty |
| Config location | `$CODEX_HOME/hooks.json`; `-c hooks.…` overrides; a `--profile` layer; `codex_hooks.…` key |
| Working directory | fresh temp dir; a directory already `trust_level = "trusted"` |
| Hook command | `cmd /c echo … >> file`; a single-token `.bat` path with no args |

That conclusion sent ACT down the file-inference path — `CodexRolloutFinder`, rollout-derived
events and counts, ~200 lines — all of it since deleted. **What it got wrong:** the last row.
A single-token `.bat` path *does* run; a **quoted** one does not, and ACT's generated command
was quoted. Upstream <https://github.com/openai/codex/issues/17532> was blamed and was never
the problem.

**The lesson worth keeping.** Every check asked "did anything arrive?", and no arrived means the
same thing whether the CLI is broken or ACT's own command is malformed. What separated them was
a probe that could only fail one way: a differently-named script per candidate shape, logging its
own name. Prefer a measurement whose failure modes are distinguishable over one that merely
answers yes or no.

## Probing a newer CLI

Still-useful mechanics, none of them hook-specific:

- **Find a runnable CLI.** The Store-packaged `codex.exe` under `C:\Program Files\
  WindowsApps\OpenAI.Codex_*\app\resources\` is readable but **cannot be executed** by a
  normal process (access denied), and `codex` is **not on `PATH`** on a desktop-app-only
  install. The runnable copy is the `CODEX_CLI_PATH` recorded in
  `~/.codex/config.toml` — `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`.
  *(This also matters for `ExecutableResolver`: resolving `codex` by name fails on such
  an install — and for ACT's task form, whose *Advanced* → agent binary field exists for it.)*
- **Type-probe the config parser.** It **ignores unknown keys**, so a wrong shape parses in
  silence and buys nothing; giving a candidate key the wrong *type* makes serde name what it
  wanted (`hooks.state = 1` → "expected a map", `hooks.SessionStart = 1` → "expected a sequence",
  `matcher = 1` → "expected a string",
  `type = "bogus"` → "unknown variant, expected one of `command`, `prompt`, `agent`"). Absence
  of an error therefore means the key does not exist, which is how Plan mode was ruled out as a
  config key.
- **Probe hook *execution* with distinct per-candidate scripts**, one per event, each logging its
  own name — one trust prompt measures the whole matrix, because the review screen counts only
  the definitions that changed.
- **A zero-token probe:** `-m <unsupported-model> exec …` initialises the session (so config
  and hooks load, and warnings print) and then fails at the model request without spending
  anything. Good for schema iteration; **not** for testing hook execution, since it aborts early.
- **`warning: failed to parse hooks config <path>`** — write deliberately invalid json to
  `$CODEX_HOME/hooks.json` to confirm a file is being read at all.

### Other tripwires hit while testing, worth not re-discovering

- `codex debug models` no longer matches the roadmap's effort table (`gpt-5.6-sol` is
  listed now), and the ChatGPT-account entitlement is narrower than the catalog:
  `gpt-5.4`, `gpt-5.6-sol` and `gpt-5.4-mini` were all rejected with *"not supported when
  using Codex with a ChatGPT account"*; `gpt-5.5` worked. Resolve models at runtime rather
  than pinning a table. **`gpt-5.4` is still `~/.codex/config.toml`'s default**, so a Codex card
  that leaves the model blank inherits one this account cannot use — the task form's default
  covers ACT's own launches, but a hand-run `codex` will hit it.
- There is an app-server JSON-RPC method **`hooks/list`** (observed in
  `~/.codex/logs_2.sqlite`) which would be the clean, non-interactive way to inspect hook
  registration and trust status. The `initialize` handshake shape was not worked out —
  `{"method":"initialize","params":{"clientInfo":{…}}}` drew no response and a subsequent
  call returned `{"error":{"code":-32600,"message":"Not initialized"}}`. Worth another
  look; it would replace most of the guesswork above.
