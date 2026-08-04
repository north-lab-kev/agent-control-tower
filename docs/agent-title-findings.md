# ACT — One-shot agent queries, and the auto-generated task title

What was measured before ACT was allowed to ask a CLI a question on its own
account, and why the flag sets in `ClaudeCodeAdapter.QueryAsync` /
`CodexAdapter.QueryAsync` look the way they do. Read this before changing either
command line, and re-run the checks at the bottom on a CLI upgrade.

Measured 2026-08-03 against **claude-code 2.1.220** and
**codex-cli 0.146.0-alpha.3.1** — the same builds `ClaudeCodeCapabilities` and
`CodexCapabilities` are pinned to.

## Why a query is not a session

The feature is small — a title written from the task's own prompt — but it needed
a second way to start a process. `IPtyHost` is wrong for it in every respect:
a query has no terminal to attach, no session to bind, no hooks to report, and
its whole output is a return value rather than a stream. Scraping an answer out
of a TUI's paint is not reading an answer, and the `prototype` branch's
`PtyStateProbe` exists to prove that.

So `ICommandHost` sits beside `IPtyHost`: same port-shaped seam, opposite
properties. This is *not* the rejected stream-json control protocol
(`ACT-overview.md` → *Rejected alternatives*). That proposal replaced the user's
interactive session with a headless one. This adds a side channel for questions
ACT asks itself, and touches no session at all.

## The two command lines

### Claude Code

```
claude -p --safe-mode --no-session-persistence --model haiku --effort low
```

with the prompt on **stdin**.

| Flag | Why |
|---|---|
| `-p` / `--print` | The one flag that makes the run non-interactive. Without it the adapter spawns a TUI with no terminal attached. |
| `--safe-mode` | Drops CLAUDE.md, skills, plugins, hooks, MCP servers, custom commands and agents. Auth, model selection, built-in tools and permissions still work normally. Every one of those is context a title question has no use for and tokens the user would pay for. |
| `--no-session-persistence` | Keeps a throwaway out of the user's `/resume` picker. Print-mode only. |
| `--model` / `--effort` | `AgentCapabilities.UtilityModel` and the bottom rung of that model's own ladder. |
| `--tools ""` | Disables every built-in tool. **Worth ~25,000 tokens a title** — see the cost table. Documented as the way to disable all tools. Must stay **last**: it is variadic, so anything after it is read as a tool name. |

**Measured:** exit 0 in ~6.7 s; **stdout is exactly the answer text** — 56 bytes
for a one-line title, no banner, no escape codes, nothing to parse.

### Codex

```
codex exec --model gpt-5.4-mini -c model_reasoning_effort="low" \
  --sandbox read-only --skip-git-repo-check --ephemeral --ignore-user-config \
  --cd <scratch> -
```

| Flag | Why |
|---|---|
| `exec` | Codex's own non-interactive mode. |
| `--ignore-user-config` | Skips `$CODEX_HOME/config.toml` — so no profile, no hooks, no MCP servers, no project instructions — while **auth still resolves from `CODEX_HOME`**. This is why no `--profile` is passed here, unlike a launch. |
| `--ephemeral` | Writes no session file. |
| `--sandbox read-only` | Refuses the tools the instruction already forbids. |
| `--skip-git-repo-check` | Required: the scratch directory deliberately is not a repository. |
| `-c model_reasoning_effort=…` | Codex has no `--effort` flag; effort is a config key and the ladder is per model. |
| `-` | Read the prompt from stdin. See the trap below — this one is not optional. |

**Measured:** exit 0 in ~4.6 s, ~5.8k tokens. **stdout is precisely the final
message.** The banner, the workdir/model/provider block, the echoed prompt, the
`thinking` lines and the token count **all go to stderr**. So there is nothing to
parse and no `--output-last-message` temp file to manage — though that flag does
exist on this build if stdout's split ever changes.

## What a title costs (measured 2026-08-03)

The prompt is not the cost; **the CLI's own preamble is**. Both numbers below come
from the CLIs reporting their own usage — `claude -p --output-format json` gives a
full breakdown, `codex exec` prints `tokens used` on stderr.

| | tokens per title | notes |
|---|---|---|
| Claude Code, haiku/low, **no `--tools ""`** | **~30,000** (`in` 10 · cache write ~4.4k · cache read 24,769 · out ~670) ≈ $0.016 | the tool schemas are most of it |
| Claude Code, haiku/low, **with `--tools ""`** | **~4,600–6,200** (`in` ~3,950 · out 650–1,140) ≈ $0.008–0.010 | what ACT ships |
| Codex, gpt-5.4-mini/low | **~6,000–7,300** | one outlier as low as 361 |

Three things this says, all of them load-bearing:

- **`--tools ""` is worth ~25,000 tokens a title** — a 6.7× cut, with no change in
  the titles produced across repeated samples. It is why the flag is there, and it
  is only usable because the prompt travels on stdin (see the traps).
- **Prompt size barely matters.** 5 words, 60 words and 1,080 words of prompt moved
  the total by ~1,500 tokens — a rounding error against the fixed preamble. There is
  no need to truncate a long prompt before asking.
- **`--system-prompt` was measured and rejected.** Replacing Claude Code's system
  prompt dropped input to 571 tokens but the output *tripled* to ~1,870 — with
  nothing telling it to be terse the model reasons far more, and output tokens are
  the expensive ones. Net cost went **up** ($0.0106 vs $0.0071). Fewer total tokens,
  worse bill: the reason to keep both numbers in the table.

Codex has no `--tools` equivalent, so it is left as measured. Anyone re-testing
should re-measure rather than trust these figures across a CLI bump — the fixed
preamble is exactly the thing a new version changes.

## Traps

- **`claude --bare` is not the switch it looks like.** It reads as the ideal
  minimal mode — "skip hooks, LSP, plugin sync, attribution, auto-memory,
  background prefetches, keychain reads, and CLAUDE.md auto-discovery" — but it
  also makes Anthropic auth *strictly* `ANTHROPIC_API_KEY` or `apiKeyHelper`, and
  **never reads OAuth or the keychain**. On a subscription install every query
  would fail to authenticate. `--safe-mode` gets the same customization-free run
  with auth intact. A test asserts `--bare` is absent.
- **Codex reads redirected stdin even when a prompt is positional.** It
  announces `Reading additional input from stdin...` and appends what it finds as
  a `<stdin>` block. Since stdin *must* be redirected (see next point), the prompt
  has to travel there too — hence `-` rather than a positional argument.
- **Standard input must be redirected and then closed.** Inherit ACT's own and
  Codex waits on a handle that never closes. Closing it immediately is what makes
  the run terminate. `CommandHostTests` pins this with a real `more`/`cat`.
- **The two streams must be read concurrently, before the wait.** A CLI that
  fills the stderr pipe while the host blocks on stdout deadlocks, and Codex
  writes far more to stderr than to stdout.
- **The streams must never be merged.** Codex's answer is one line on stdout;
  merged with stderr it becomes a transcript. `CommandResult` keeps them apart.
- **A `.cmd`/`.bat` cannot be started with `UseShellExecute = false`** — Windows
  answers 193 — and a global npm install of either CLI is exactly that shim.
  `CommandHost` routes those through `cmd /c`, which is only safe because the one
  argument carrying arbitrary text goes on stdin instead of the command line.
- **`--tools ""` is variadic and must be last.** Placed before a positional prompt
  it swallows the prompt as a tool name — which is why it was initially left out
  and only became usable once the prompt moved to stdin. Anything appended after it
  in `QueryAsync` disappears the same way; a test pins it as the final argument.
  Note also that an **empty-string argument** has to survive `ProcessStartInfo`'s
  own quoting to reach the CLI as `""`; it does, and the end-to-end run above is
  what proves it.
- **`CommandHost` assigns the environment rather than merging it.** A caller that
  passes an empty dictionary gets a process that cannot find anything on `PATH`.
  `AgentEnvironment.ForQuery` inherits, which is what every caller wants.
- **The working directory must be empty, and must exist.** Both CLIs read what
  they start in — CLAUDE.md, AGENTS.md, project settings, the enclosing git repo.
  `IAgentConfigFiles.ScratchDirectory()` is an ACT-owned empty directory created
  on demand; nothing is ever written into it. A directory that does not exist
  fails at spawn.

## What the title itself is held to

`TaskTitleQuery` (in `Act.Core/Agents`) owns both halves, and both are pure:

- **The instruction** is localised like everything else ACT writes to an agent,
  so a French board does not fill up with English titles. It asks for eight to
  fifteen words, caps at thirty, and forbids tools, quotes, markdown and preamble.
  It also asks for *the detail that tells this task apart from a similar one* —
  without that, a longer target buys nothing: the models happily pad four words to
  twelve, and a board of near-identical strips is worse than a board of terse ones.
- **The cleaning** is the interesting half, because the failure mode is a chatty
  model rather than a broken process: markdown fences, heading and bullet marks,
  `**emphasis**`, matched quotes of five kinds, brackets, a trailing period, and
  an explanation on the following lines. The first non-empty line wins, because
  a model that explains itself does so *after* the answer far more often than
  before it. A trailing `?` or `!` is kept — a title that asks something is
  saying something; only a full stop is decoration.
- **Two limits, not one.** Thirty words catches a model ignoring the brief;
  200 characters (the form's own `MaxLength`) catches a single enormous token,
  which is a model pasting a path or a url.
- **It never fails.** `TaskTitleQuery.FromPrompt` — the prompt's own opening
  words — is the floor when the CLI is missing, refuses, times out, or answers
  with nothing usable. A save refused over a field ACT offered to fill in would
  be worse than the form that made the user type a title.

## Verified by hand

Against a real board, with both CLIs installed (2026-08-03):

| Path | Result |
|---|---|
| Button, Claude Code, prompt about badge colours | `Centralize flight badge colors` in ~7 s, no toast |
| Save with an empty title, Claude Code | Card #1000 created as `Consolidate flight strip badge colors` |
| Button, Codex, prompt about a dark theme toggle | `Add Dark Theme Toggle to Settings` in ~5 s, no toast |
| Button, Codex binary set to a path that does not exist | Fell back to the prompt's opening words, `Win32Exception` logged at warning, and the "Could not reach the agent" toast shown |

## Re-test checklist on a CLI upgrade

1. `claude --help` — is `-p`, `--safe-mode`, `--no-session-persistence` still
   spelled that way? Has `--bare` gained OAuth support (if so it becomes the
   better switch)?
2. `codex exec --help` — are `--ignore-user-config`, `--ephemeral`,
   `--skip-git-repo-check` still there, and does `-` still mean stdin?
3. Run each command line by hand with stdout and stderr **redirected to separate
   files** and confirm stdout still holds the answer alone. This is the one that
   silently breaks: a CLI that starts writing a banner to stdout turns every
   generated title into a paragraph.
4. Confirm `AgentCapabilities.UtilityModel` still names the cheapest model each
   agent offers.
5. **Re-measure the cost table.** `claude -p --output-format json` reports its own
   usage; `codex exec` prints `tokens used` on stderr. The fixed preamble is exactly
   what a new CLI version changes, so a figure carried over untested is a figure
   that is wrong.
