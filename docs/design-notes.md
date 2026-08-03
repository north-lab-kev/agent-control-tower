# ACT — design notes: how the non-obvious decisions were reached

**What this file is for.** The code says what it guarantees; this file remembers *how we found
out*. Measurements against a pinned CLI version, shapes that were tried and rejected, classes that
were deleted and why — all of it is worth keeping and none of it belongs in a comment beside the
code, because prose that outlives its subject is worse than no prose. Two dead members were found
in the 2026-08-02 review precisely because their comments were still confidently describing
behaviour that had been deleted.

**The rule the comments follow.** A comment may *cite* a measurement in a clause — "measured
2026-07-30, ~0.8 s" — but it may not *narrate* one. Anything longer than a clause, anything about
a previous implementation, and anything that would be wrong if the code changed lives here.

**Companion files**, which already do this for their own areas and are not duplicated below:

- `agent-usage-findings.md` — the two usage endpoints, their response shapes, the unit and
  encoding traps, and the alternatives measured and rejected.
- `codex-hooks-findings.md` — Codex hook discovery, the TOML shape, the quoting bug, hook trust.
- `repository-structure.md` — *Key decisions*: project boundaries, why `Act.Desktop` is not a
  project, the Electron startup-timing trap, store conventions.

---

## Rules and scheduling

### A tool event may not clear a permission block — measured 2026-07-31

A Codex card sat on `running` with a Bash approval still on screen: a tool event arrived *after*
the `PermissionRequested` and was indistinguishable from a keystroke. Either the CLI reports the
previous tool late, or the posts race — one `curl` per hook, so ACT sees arrival order, not
emission order. The rule holds either way.

The discriminator is `ToolName`, because it is the only thing separating the two events:
`UserPromptSubmit` carries none, every tool event does.

**The cost, chosen deliberately over the alternative.** Approving a prompt in the terminal fires no
`UserPromptSubmit`, so an approved card keeps saying `needs permission` until the turn ends. A
stale *"you are needed"* is a wasted glance; a stale `running` hides a session waiting on a human,
which is the one thing the board exists to prevent.

**`needs answer` is deliberately not covered, and the asymmetry is the point.** A question is
raised by its tool's `PreToolUse` and *answered* at its `PostToolUse`, so there the tool event
really is the user acting. A permission has no such paired event — nothing reports the approval —
which is exactly why only that half needs the guard.

### The quiet threshold is 15 minutes — measured across 2,729 gaps

Within-turn gaps in real Claude Code transcripts: p50 1.3 s, p90 7.2 s, p99 45 s. Every gap past
three minutes turned out to be a session waiting on its human rather than working. Fifteen minutes
is ~20× the p99 — far outside normal chatter, still short enough to notice overnight.

It replaces the `stale` badge and its watchdog, **deleted 2026-07-31**. The badge claimed something
no signal supported and overwrote the one thing ACT did know; the chip states the gap instead of
guessing at its cause. See the spec's *No stale badge — the quiet chip instead*.

### The pre-session prompt is reported by silence — measured 2026-07-30

Both CLIs open on a directory-trust prompt for a working directory they have not seen, and it
blocks *before* the session exists, so no hook can report it. Measured against `claude-code`: with
the trust prompt on screen **not a single hook fires** for as long as it is left there, not even
`SessionStart`, while a directory the CLI already trusts produces its first hook **~0.8 s** after
the spawn.

So the absence is the signal, and `PtyAgentSession.StartupGrace` is 8 s — an order of magnitude of
headroom. Overshooting costs a slow start two extra transitions and nothing else: the first real
hook is activity and puts the card back.

---

## The command line

### Windows argument quoting — measured 2026-07-30

Windows has no `argv`. A process receives one command-line *string* and each runtime re-splits it,
so whoever builds that string owns the escaping — and `Porta.Pty`'s own escaping doubles quotes
(`"` → `""`), which Claude Code's parser does not read back as a literal quote. It truncates the
argument there instead.

**What that cost, and why it went unnoticed for so long.** ACT's injected preamble contained
`{ "state": "ready_for_review" }`, so every launch handed Claude Code an argument that ended at
that first quote — 677 characters of preamble and **no task prompt at all**. The agent then greeted
the user and ended its turn, which looked enough like success that the hooks, the transitions and
the board all agreed nothing was wrong.

The fix takes the job over rather than escaping on top of it: `VerbatimCommandLine` passes the
command line through untouched and ACT quotes each argument by the rules `CommandLineToArgvW`
documents. Measured against the real CLI afterwards: **226 characters in, 226 out**, where the
transport's own escaping gave 28.

---

## Ingestion

### Codex transcript guessing is gone — deleted 2026-07-31

Codex had a second way for ACT to find its transcript: `CodexRolloutFinder` inferred the file from
the folder layout and a cwd/timestamp match, written because its hooks were believed dead. They are
not — that was ACT quoting the hook command, see `codex-hooks-findings.md` — so the guessing was
deleted along with `ITranscriptFinder` and `ITranscriptDirectory`. Both agents now learn where
their transcript is the same way: the hooks say so.

**The accepted cost:** a user who answers Codex's hook-review screen with *"Continue without
trusting"* gets no payloads, so nothing names the transcript and that card reports only what its
process can say.

### The transcript is polled, not watched

A `FileSystemWatcher` on an appended file needs its own debounce and reports late anyway. A read
from a stored offset once a second is a few kilobytes.

### The Codex normalizer used to report liveness — moved to hooks 2026-07-31

It reported activity, turn ends and the turn/tool counts, because the hooks were believed not to
fire. All of that moved to the hooks, which report first-hand instead of a poll behind a file, and
which count the same things Claude Code's do. Hooks own the counts; the transcript owns enrichment.

**One event stayed, and it is a real exception:** `TurnFailed`. A `task_complete` carrying an
`error` is the CLI saying the turn failed while its process stays alive and would exit zero — the
failure `ProcessExited` cannot see — and no hook has been *observed* reporting it. Measured, not
assumed: drop it only after watching a real failed turn's `Stop` payload.

---

## Capability lists

Both lists are pinned snapshots, and re-checking them belongs on the CLI-upgrade checklist.

### Claude Code — checked against 2.1.220

Hard-coded because the CLI offers nothing better. Checked against `claude --help`: there is **no
models subcommand** — an unrecognised argument is read as the *prompt*, so `claude models` starts a
session and answers the question — and `--model` documents only that it takes an alias, naming
`fable`, `opus` and `sonnet` as examples. The one list the CLI does enumerate is `--effort (low, medium, high, xhigh, max)`, and it
is not worth scraping help text for — a wording change would fail back to the pinned list silently
and leave the staleness it was meant to fix.

**Context windows are measured, not guessed.** They are read out of the CLI's own model table in
`claude.exe` 2.1.220: each entry carries `context: { window }`, giving 1e6 for `claude-opus-5`,
`claude-sonnet-5` and `claude-fable-5`, and 200000 for `claude-haiku-4-5`. A model the list does not
know shows no percentage at all, which is the intended failure — the alternative is a bar measured
against the wrong window.

Two ways the CLI's *effective* window ends up smaller, neither of which ACT can observe:
`CLAUDE_CODE_MAX_CONTEXT_TOKENS` overrides it, and a session whose 1M credits are blocked falls
back to 200k. Both make ACT's percentage read low, never high.

### Codex — transcribed from `codex debug models` on 0.146.0-alpha.3.1

Each model carries a different effort ladder, which is why `AgentCapabilities` models effort per
model rather than per agent. `codex-auto-review` is omitted: the catalog marks it
`visibility: hide`, so it is not a model a user picks.

**Pinned on purpose, and staying that way.** Codex does publish this list at runtime — `codex debug
models` renders it, and the CLI caches the same JSON at `$CODEX_HOME/models_cache.json` — but
neither is a contract ACT is party to. An internal cache whose schema can move under a CLI upgrade
would fail back to the pinned list silently, which is the same staleness wearing a costume. A
pinned list is at least honest about being a snapshot.

**`PermissionMode.Auto` is deliberately absent.** Codex has no classifier tier, so `CodexPermissions`
resolved it to the *same* `on-request` + `workspace-write` pair as `AcceptEdits`, and recorded an
adjustment at launch to admit it. Offering both put two identical choices in the form, one of them
described as *"a model classifier approves or denies, never asks"* — which Codex does not do and
which is the opposite of what `on-request` means. Not offering it is the honest version of the same
fact.

---

## The web host

### No HTTPS redirection and no HSTS

ACT is a desktop app whose web host is an implementation detail: the UI is a local window under
Electron, there is no certificate to serve and nothing reaches it from off the machine. Both were
template defaults, and both were worse than inert here.

- **Redirection** logged *"failed to determine the https port"* on every start under the http
  profile; under the https one it would have found a port and bounced every plain-http request to
  it — **including the agents' hook posts on the loopback port**. A hook client does not follow a
  307, so ingestion would have died quietly on that profile.
- **HSTS** only ever applied to the packaged build, where a policy pinned to localhost is a
  liability to every other local app on the machine rather than a protection for this one.

### Two listeners on one host

Kestrel makes this awkward: configured addresses and explicit `Listen` endpoints are mutually
exclusive, and touching either one discards the other. **The first two attempts at this both moved
the whole UI onto the hook port.** So the app's own address is read back out of configuration and
re-added alongside the hook one — `urls` is the key both `applicationUrl` and `UseUrls` end up
writing to, which is what makes it work under `dotnet run` and under the Electron shell alike.

Status-code pages have to be disabled per hook request, too. Without that the app re-executes a
rejection through the Blazor pipeline, which then answers a json post with *"incorrect
Content-type"* — a 400 that says nothing true about what happened.

---

## Background work

### Five pumps, one lifetime — consolidated 2026-08-02

`QueueRunner`, `SessionEventPump`, `TranscriptPump`, `UsagePump` and `RetentionPump` each wrote out
their own cancellation source, loop, catch policy and disposal. The copies had drifted into
disagreeing on the two things that matter, and both disagreements were bugs:

- `QueueRunner` and `RetentionPump` disposed their token source and semaphore while a pass could
  still be awaiting them — `ObjectDisposedException` on the way out of the app.
- `SessionEventPump`'s flush loop caught only `OperationCanceledException`, so one store failure
  inside a flush would have ended the metrics flush for the life of the process.

`Act.App/Hosting/BackgroundWork` now owns all of it: cancel, wait for what was started, then
dispose — and a guard around each *pass* rather than around the loop, so a failed pass is reported
and the next tick tries again.
