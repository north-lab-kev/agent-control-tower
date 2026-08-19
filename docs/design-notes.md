# ACT — design notes: how the non-obvious decisions were reached

**What this file is for.** The code says what it guarantees; this file remembers *how we found
out*. Measurements against a pinned CLI version, shapes that were tried and rejected, classes that
were deleted and why — all of it is worth keeping and none of it belongs in a comment beside the
code, because prose that outlives its subject is worse than no prose. A review once found two dead
members precisely because their comments were still confidently describing behaviour that had been
deleted.

**The rule the comments follow.** A comment may *cite* a measurement in a clause — "measured
against 2.1.220, ~0.8 s" — but it may not *narrate* one, and it names the CLI version rather than
a date. Anything longer than a clause, anything about a previous implementation, and anything that
would be wrong if the code changed lives here.

**Companion files**, which already do this for their own areas and are not duplicated below:

- `findings/agent-usage.md` — the two usage endpoints, their response shapes, the unit and
  encoding traps, and the alternatives measured and rejected.
- `findings/codex-hooks.md` — Codex hook discovery, the TOML shape, the quoting bug, hook trust.
- `repository-structure.md` — *Key decisions*: project boundaries, why `Act.Desktop` is not a
  project, the Electron startup-timing trap, store conventions.

---

## Rules and scheduling

### A tool event may not clear a permission block — measured

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

It replaces the `stale` badge and its watchdog, both **deleted**. The badge claimed something
no signal supported and overwrote the one thing ACT did know; the chip states the gap instead of
guessing at its cause. See the spec's *No stale badge — the quiet chip instead*.

### The pre-session prompt is reported by silence — measured

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

### Windows argument quoting — measured

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

### A variadic flag must never be the last one before a positional — three times now

The prompt is a positional argument on both CLIs, and both have variadic flags that will eat it.
`--tools` was the first (see `ClaudeCodeAdapter.QueryAsync`), `codex --image` the second, and
`claude --add-dir` the third — and the third **shipped broken**, because the command line *looked*
right when it was read back off the process table and nobody checked that the agent had actually
received anything.

The failure is silent in the shape that matters. Non-interactively the CLI says `Error: Input must be
provided either through stdin or as a prompt argument when using --print`; in a TUI launch it says
nothing at all — the agent comes up with an empty composer, so ACT's own board reports a running card
and the transcript never starts. The symptom was seen during verification, mistaken for the CLI's
"manual mode", and explained away.

The rule, for any flag added to a launch command line from here on:

- **Bind the value with `=`** (`--add-dir=<path>`, `--image=<path>`) so the option takes exactly one
  value per occurrence, whatever its arity.
- **Verify the agent received the prompt**, not that the command line contains it. `claude -p
  --model haiku --add-dir=<dir> "Reply with exactly: BANANA"` answering `BANANA` is the check;
  reading the arguments back proves only that ACT spelled them, not that the CLI parsed them.
- The adapter tests pin the *form* — the bound spelling present, the bare one absent, the prompt still
  the final argument — because a unit test cannot re-derive commander's or clap's parsing.

### `codex --image` must bind its value with `=` — measured

`-i, --image <FILE>...` is variadic, so `-i C:\a\shot.png "the prompt"` does **not** mean one image
and a prompt: clap keeps collecting positionals into the image list, the prompt is swallowed as a
second file name, and Codex then prints `Reading prompt from stdin...` and blocks on a prompt that
will never come. Measured against `0.146.0-alpha.3.1`, where it cost a launch that looked like a
hung CLI.

`--image=<path>` binds exactly one value per occurrence, so several images are several flags and the
positional prompt survives at the end where both adapters put it. This is the same trap `--tools`
sets for `ClaudeCodeAdapter.QueryAsync`, and the same answer: **keep the prompt away from a variadic
flag's value.** `CodexAdapterTests` pins the `=` form and asserts the prompt is still the last
argument, because nothing in the flag's own help says any of this.

### Attachments travel as paths, never as contents

The opening prompt is a positional command-line argument for both agents, and Windows caps a command
line at ~32,767 characters. Inlining an attached file's *contents* would therefore spend the whole
budget on a modest log and fail the spawn outright on a large one — the same class of failure the
quoting bug above produced, and just as quiet. So `AttachmentInstruction` writes one **absolute path
per line** under a localised header and the agent reads the file itself, which is the thing a CLI
host can do that a chat client cannot: the file is already on the machine the agent runs on.

Claude Code needs `--add-dir` with it, because the paths are deliberately outside the working
directory and `Read` on one is exactly what `default` permission mode stops to ask about — an
unattended task would park on a prompt for its own attachment. Measured: the flag adds no trust
prompt of its own, and an agent handed a path under `%LOCALAPPDATA%\ACT\attachments\<cardId>\` reads
it without stopping. Codex needs no grant at all: it reads outside `--cd` under both `read-only` and
`workspace-write`, also measured.

### A pasted screenshot may not be in `clipboardData.files` — measured

Read off a real Windows clipboard, a screenshot is `PNG` + `Bitmap` + `DeviceIndependentBitmap` +
`Format17` and **no `FileDrop`**: no path, no filename, nothing to reference. That is the measurement
behind *a copy, not a reference*, and it is also why the pasted-bitmap rename exists — the name a
page sees is one the browser invented.

It is also why `act-attach` reads `clipboardData.items` and not only `.files`. A bitmap with no
backing file can arrive as a `kind: 'file'` **item** alone, so a handler that trusts `files` attaches
a dropped file happily and does nothing at all for the one thing people actually paste — silently,
with no error to notice.

**The `items` fallback yields to text, and that guard is the whole subtlety.** Copying a cell from
Excel or a selection from Word puts a bitmap *and* text on the clipboard; claiming that paste would
attach a picture of the cell instead of pasting its text into the prompt box or the terminal. So the
fallback is skipped whenever `text/plain` is non-empty. A populated `files` needs no such guard —
that is an unambiguous file paste, the shape a file copied in Explorer arrives as. Four cases,
all four verified in the app: bitmap-only → attach; bitmap + text → text; `files` populated →
attach; plain text → text.

**`files` wins over `items`; the two are never concatenated.** A screenshot tool puts the same
picture on the clipboard *twice*. Measured off a Screenpresso capture: `FileDrop` (a real
`…\Screenpresso\2026-08-04_14h46_07.png`, 180,964 bytes), `Bitmap` (2064×1078, the same image raw),
`FileContents`/`FileGroupDescriptorW`, an HTML `<img src="file:///…">`, and **no** `UnicodeText`.
Reading both sources would attach one screenshot as two files; preferring `files` also keeps the
tool's own filename instead of the one the browser invents, which is why this shape lands as
`2026-08-04_14h46_07.png` while a bare bitmap lands as `pasted-<timestamp>.png`.

**The HTML is never parsed, and that is a boundary rather than an omission.** The same capture's
`<img src="file:///C:/…">` names a real local path, and honouring it would let whatever the user
pasted nominate any file on the disk for ACT to copy and hand to an agent. Only real `File` objects
the browser itself put in `files`/`items` are taken — the browser has already gated those.

### The hover thumbnail has to be `position: fixed` — measured

An overlay anchored to an attachment chip with `position: absolute` is clipped by the task sheet,
because `.body` is the page's scroll container (`overflow-y: auto`) and absolute positioning does not
escape an ancestor's overflow. Both directions were measured in a 1280×720 window: opening *downward*
put the box's bottom at 865 against a scroller ending at 667 — about 200px cut off — and opening
*upward* fails the same way on a taller form, where the chips sit higher. There is no safe fixed
direction, which is what rules out the CSS-only version.

So the box is `position: fixed` (outside every ancestor's clip) and `act-attach.place` gives it
viewport coordinates: below the chip when there is room, above when there is not, clamped on both
axes. Verified on three viewports — flipped above at 720px tall, below at 1646px, and pulled back to
an 8px right-hand gap for a chip shoved against the right edge.

**The box is sized in CSS, not by the image**, and that is what keeps it to one pass. An `<img>` has
no intrinsic size until it loads, so a box that grew with its content would have to be positioned
again afterwards — a visible jump on every hover. A fixed 22rem × 16rem frame with
`object-fit: scale-down` is measurable before the first byte arrives, shrinks a screenshot to fit and
leaves a favicon at its own size instead of blowing it up.

### Inserting a dropped file's path is not the send-back that was cut

Send-back (deleted) composed a message and pressed the agent's submit key, which put ACT
in the business of guessing when a TUI was ready to be typed into. A file dropped on the Terminal tab
does neither: it inserts **the file's own path**, quoted, where the cursor already is, and sends
nothing. Every terminal emulator does this with a dragged file, and the user still has to read what
landed and press Enter. `IAgentTerminal`'s doc comment was amended rather than quietly contradicted —
the invariant is that ACT composes no *instruction*, not that it never writes.

### A recorded executable path that stopped existing is replaced, not protected — measured

`AgentInstallDiscovery` records the path of a CLI it finds off `PATH`, because a pty spawns an
explicit image and would otherwise fail with "not found" for a binary sitting right there. The
original rule was that it only ever *fills an empty setting*, on the stated grounds that a path is
the one answer that came from a human. **Codex breaks that rule by upgrading.** It installs under
`%LOCALAPPDATA%\OpenAI\Codex\bin\<build-hash>\codex.exe`, the hash changes with every update, and the
path ACT itself wrote is then dead — while the setting is no longer *empty*, so the guard skipped it
and every Codex launch failed at spawn until the path was re-pasted by hand.

Measured 2026-08-19: a fresh store discovered `…\bin\f71e347eb70b3d24\codex.exe` correctly, so the
lookup was never the problem — `DirectoriesNewestFirst` already handles the hash directory, and it
skips the sibling folders holding `node.exe` and `rg.exe`. Only the *recorded* value went stale.

So the rule is now about the path's **state**, not its authorship, which ACT cannot know anyway:
empty is filled, a bare name is never touched (no separator means "resolve on `PATH`", the same rule
the launch follows), a path that still exists is left alone, and a path that has stopped existing is
replaced — at `Warning`, because overwriting something a human may have typed is not a thing to do
quietly. Nothing is blanked out when discovery finds nothing: a dead recorded path is still the best
guess anyone has, and a CLI mid-upgrade comes back.

### A session ACT was launched from must not be passed on to the agents it spawns — measured

`AgentEnvironment` copies ACT's own process environment into every CLI it starts, and that is right
for `PATH`, a proxy, or credentials. It is wrong for the variables a *Claude Code session* sets for
its children: ACT started with `dotnet run` from inside a session — which is how ACT is developed and
verified — inherits them, hands them to the `claude` it spawns, and that CLI comes up believing it is
a nested session rather than a fresh one.

**Measured, in a `claude-desktop`-hosted session:** `CLAUDECODE=1`, `CLAUDE_CODE_ENTRYPOINT`,
`CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_HOST_SESSION_ID`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_PID`,
`CLAUDE_AGENT_SDK_VERSION`, `CLAUDE_CODE_OAUTH_SCOPES`, both `CLAUDE_CODE_SDK_HAS_*_REFRESH`, and
five internal feature flags. Seventeen variables, of which the workaround this replaced neutralised
six.

That count is the argument for the prefix. **The set differs by host and by release** — a terminal
session carries fewer than a desktop one, and `CLAUDE_CODE_REPORT_FINDINGS` is plainly not a variable
that existed a year ago. A named list of the six that were noticed is one CLI release from letting a
new marker through in silence, so `ClaudeCodeEnvironmentScrub` removes `CLAUDECODE` and everything
prefixed `CLAUDE_`.

**It lives with the adapter, not in `Act.Core`.** Which variables one CLI must not inherit is a fact
about that CLI, and the first draft of this put it in `AgentEnvironment` — where it would have been
Claude Code's private contract sitting in the project that is supposed to know nothing about either
vendor. So `Act.Core` owns the seam and nothing else: `IEnvironmentScrub`, one call, applied to the
inherited environment *before* ACT's own variables and before the install's overrides. The parameter
is spelled at every call site rather than defaulted, because an agent that subtracts nothing is a
claim, and a claim should be visible at the point it is made.

**Removed, not set to `0` or `""`.** The CLI's own code paths are written against these being
*absent* on a first launch; whether it reads `CLAUDECODE=0` as falsy is an assumption nobody has
tested, and an empty `CLAUDE_CODE_ENTRYPOINT` is a value the CLI never sets itself. Deleting the keys
reproduces exactly what a top-level launch sees, which is the only shape that needs no assumption.

**Gated on `CLAUDECODE` rather than unconditional, and the gate is what makes the prefix safe.** The
marker's presence *is* the detection — there is no other way those variables reach ACT — so its
absence means ACT was launched normally and a `CLAUDE_*` in its environment is the user's own machine
config. Wiping that would silently undo a setting they made. Inside a session the two are
indistinguishable and a machine-wide knob goes with the markers; the install's own `Env` is where it
comes back, applied after the scrub so an override still wins.

**`CLAUDE_CONFIG_DIR` is the one exemption**, because ACT reads it itself:
`ClaudeCodeUsageDialect.DefaultCredentialsPath` honours it when locating `.credentials.json`. A CLI
that could not see it would authenticate against a different install than the usage meter reports on.

**What is still unmeasured, and what to re-test on a CLI bump.** Which of the seventeen actually
changes the child's behaviour was never isolated — the original workaround was assembled until a
symptom stopped, and this entry generalises it rather than proving it. Codex's equivalents were not
measured at all, so nothing `CODEX_*` is touched. The check is the one from
`docs/findings/agent-title.md`: spawn a task from an ACT that was itself started inside a session and
confirm the agent opens a session of its own.

---

## The pty

### A pseudo-console outlives the process attached to it

`IPtyConnection` is `IDisposable`, and ending the agent without disposing the connection leaks one
`conhost.exe` per session ACT ever launches — found by watching the process tree after a session
ended. `PtyProcess.DisposeAsync` disposes the connection last, and disposal is the only teardown a
session has, so no path can skip it.

### A pty does not walk `PATH`

It spawns with an explicit image path, so a bare `claude` / `codex` is looked for in the working
directory and fails. Resolution lives in `Act.Infrastructure/Terminal/ExecutableResolver` — the one
layer allowed to know about `PATHEXT`.

### Rejected liveness routes

Three alternatives to hosting the real TUI under a pty were built or probed and rejected:

- **stream-json control protocol** (`claude -p --input-format stream-json`) — ACT holding the
  pipes and answering control-requests itself. Rejected for the reason that outranks its
  elegance: it **replaces the interactive session the user actually wants** with a headless one,
  forcing ACT to re-implement every prompt surface the TUI already renders well — permission
  dialogs, AskUserQuestion, plan approval, `/`-commands — and to keep re-implementing them as the
  CLI evolves. It is also underdocumented, so each re-implementation would be pinned to an
  unversioned protocol. Kept on the table as a possible future unattended mode, where there is no
  human to hand a terminal to and re-implementing prompts is moot.
- **Desktop-only handoff as the launch mode** — `claude://code/new?q=…` prefills but **never
  auto-sends** (deliberate safety), registers none of ACT's hooks, and writes to an opaque store.
  A card launched that way would have no badges, no metrics and no completion signal. Hence
  handoff is an *action on an observed session*, not a way to start one.
- **Desktop GUI automation** (synthetic Enter / Playwright) — Electron's single-instance lock
  strips `--remote-debugging-port` (breaks Playwright/CDP attach); synthetic keystrokes need OS
  Accessibility permission and fight focus/timing races. It only "helps" the unattended case,
  which a non-prompting `permissionMode` handles cleanly.

## The MCP server

### `[McpHeader]` is not how a tool reads an HTTP header — measured on SDK 2.1.0

A parameter carrying the attribute is still published in the tool's input schema, so the model sees
a `token` argument it cannot supply and every call fails. ACT reads the header off
`IHttpContextAccessor` instead, and `FollowUpTests` pins the tool schema so the mistake cannot come
back.

### Streamable HTTP on the hook port, never stdio

stdio would mean one child process per session, and ACT has already paid once for leaking a process
per session (the `conhost` leak above). The server rides the existing hook listener at `/mcp`,
reuses `x-act-hook-token`, and resolves the token **per request** rather than at initialize — a
streamable-HTTP session is long-lived and `SessionRegistry.Forget` releases the token when the
session ends, so a token read once at connect would let a dead card keep a working connection.

## Ingestion

### Codex transcript guessing is gone

Codex had a second way for ACT to find its transcript: `CodexRolloutFinder` inferred the file from
the folder layout and a cwd/timestamp match, written because its hooks were believed dead. They are
not — that was ACT quoting the hook command, see `findings/codex-hooks.md` — so the guessing was
deleted along with `ITranscriptFinder` and `ITranscriptDirectory`. Both agents now learn where
their transcript is the same way: the hooks say so.

**The accepted cost:** a user who answers Codex's hook-review screen with *"Continue without
trusting"* gets no payloads, so nothing names the transcript and that card reports only what its
process can say.

### The transcript is polled, not watched

A `FileSystemWatcher` on an appended file needs its own debounce and reports late anyway. A read
from a stored offset once a second is a few kilobytes.

### The Codex normalizer used to report liveness — moved to hooks

It reported activity, turn ends and the turn/tool counts, because the hooks were believed not to
fire. All of that moved to the hooks, which report first-hand instead of a poll behind a file, and
which count the same things Claude Code's do. Hooks own the counts; the transcript owns enrichment.

**One event stayed, and it is a real exception:** `TurnFailed`. A `task_complete` carrying an
`error` is the CLI saying the turn failed while its process stays alive and would exit zero — the
failure `ProcessExited` cannot see — and no hook has been *observed* reporting it. Measured, not
assumed: drop it only after watching a real failed turn's `Stop` payload.

Reproducing it is narrower than it looks: it needs a model the **CLI accepts but the account
cannot use** (the original measurement's `gpt-5.4`), so a session starts and the *request* is
rejected. A client-side-invalid name exits with code 2 before any session exists — that path is
`ProcessExited` → `error`, not `TurnFailed` — and `OPENAI_BASE_URL` is ignored under ChatGPT
auth, so pointing it at an unreachable host does not fail a turn either.

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

## The desktop shell

### The splash screen is the Electron host's, not ACT's

The splash is a PNG named by `<ElectronSplashScreen>` in `Act.App.csproj`. The ElectronNET.Core
build targets copy it into the `.electron` output and write it into the host manifest's
`splashscreen.imageFile`; the host's `main.js` then shows it inside `app.on('ready')` — before the
socket bridge is up and, in the packaged build, before the .NET backend has even been spawned.
That earliest-possible paint is the whole point, and it is why the splash is **not** a
`DesktopShell` window: anything C# creates has to wait for the web host to boot, the board to load
and the Electron bridge to connect, which is most of the time the splash exists to cover.

The price, accepted deliberately (speed was chosen over placement):

- **It always centres on the primary display.** The manifest splash knows nothing about the
  remembered window bounds, so on a multi-monitor setup it may flash on a different screen than
  the one the main window then opens on.
- **It dies at window *creation*, not window *show*.** `main.js` destroys it on the first
  `browser-window-created`, and `DesktopShell` creates the main window hidden and shows it at
  `ready-to-show` — so there is a blank gap while the Blazor page loads. Nothing on the C# side
  can extend the splash across it; the handler is the package's.
- **Size comes from the image.** The manifest's `width`/`height` fields exist in `main.js` but the
  package template never writes them, and an `.html` splash falls back to a fixed 800×600 — which
  is why the splash is a PNG (window is sized to its pixel dimensions, transparent, frameless) and
  not a themable page.

The image itself is generated, not drawn by hand: dark title-bar gradient, the favicon mark, the
wordmark's tracking and the mono full name, matching `MainLayout`'s brand strip. One language is
baked in; that loses nothing today because `Shell_FullName` is identical in both resx files.
`SplashScreenTests` pins the csproj property, the file's existence, and that the PNG stays within
the smallest work area the app itself accepts (1000×320), since nothing compiles against any of it.

### The Linux build carries its own ICU

The first AppImage anyone ran on a real distro never left the splash screen, and left nothing behind
to say why: no log file, no dialog, no `~/.local/share/ACT` at all. Electron was entirely healthy —
main, zygote, GPU and renderer processes all up — and there was simply no .NET process beside them.
Run by hand, `resources/bin/Act.App` said it: *Couldn't find a valid ICU package installed on the
system*.

Two facts about that failure decide everything else. It is `Environment.FailFast` from
`GlobalizationMode`'s static constructor, so **no managed code can catch it**; and it happens inside
`WebApplication.CreateBuilder(args)`, which is six lines *above* `AddActFileLog` — so the file log
that would have named the cause did not exist yet. Windows never showed this because Windows 10+
ships ICU in the OS; Linux ships nothing, and neither `SelfContained` nor `PublishSingleFile` bundles
it.

`InvariantGlobalization` was the cheap fix and was rejected: it makes every culture behave as the
invariant one, which would flatten the localised dates, numbers and sort orders ACT ships two resx
files for. So the app carries ICU instead, via `Microsoft.ICU.ICU4C.Runtime` and the
`System.Globalization.AppLocalIcu` switch — about 36 MB uncompressed, nearly all of it
`libicudata.so`.

Three details are load-bearing:

- **Linux RIDs only.** The package reference and the switch sit in an `ItemGroup` conditioned on
  `$(RuntimeIdentifier)` starting with `linux`, so the Windows installer neither grows nor changes
  the ICU it already finds in the OS.
- **One version string.** `ActIcuVersion` in `Directory.Build.props` feeds both the `PackageVersion`
  and the switch's value, because the runtime looks for exactly the version the switch names. A bump
  that moved only one of them would reproduce the original silent hang.
- **Nothing patches `AppRun`.** `package-desktop.ps1` publishes RID-specific, which flattens
  `runtimes/linux-x64/native/*.so` into the publish root — and that root *is* `resources/bin` inside
  the AppImage, which is where the app-local probe looks. No `LD_LIBRARY_PATH`, no custom launcher.

Verified once, by loading rather than by starting: with `libicuuc.so.78` present system-wide,
`/proc/<pid>/maps` showed the process had mapped only the three `.so` files from the app directory.

Nothing guards it since. A CI check was written and deliberately dropped as not worth its complexity,
so the trap is live and worth stating plainly: the version bump that moves the `PackageVersion` and
not the switch — or the reverse — reproduces the original failure exactly, silent and logless. Note
also that a check on the runner would have to run in a container to mean anything, because
`ubuntu-latest` has libicu of its own and would pass whether or not ACT bundles a thing.

Bundling ICU fixes this cause, not the class: any backend crash before `AddActFileLog` still shows an
eternal splash with no diagnostics, and since the ICU abort proved managed code cannot intervene, a
real fix belongs in the Electron host — notice the child process died, surface its stderr.

### Five pumps, one lifetime

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

---

## Logging

### Four traps between MEL and Serilog — measured

ACT logs through `Microsoft.Extensions.Logging` and keeps Serilog behind
`Act.Infrastructure/Logging/`. Getting that boundary to actually hold turned up four things,
none of them visible from the code that remains.

**`AddSerilog()` takes the level config away from `appsettings.json`.** It does
`AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace)`, which is deliberate on Serilog's part —
its own `MinimumLevel` becomes the single authority — and is exactly backwards for a codebase whose
whole reason for using MEL is that the provider is replaceable. So `AddActFileLog` registers
`SerilogLoggerProvider` itself, the Serilog logger sits at `MinimumLevel.Verbose()`, and
`Logging:LogLevel` does all the filtering. **`MinimumLevel.Verbose()` is not a debug leftover** —
lower it and `appsettings.json` silently stops being able to raise the level.

**`ILoggingBuilder.AddProvider(instance)` leaks the file handle.** The container never disposes an
instance it did not create, so the sink is never flushed or closed; the tell was a test whose
`TempDirectory` could not delete the log file. Registering through a factory
(`AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(logger, dispose: true))`) makes the
container the owner, and the shutdown lines really do reach the file.

**`{Message:lj}` quotes strings.** The `j` wins for value rendering, so a template argument came out
as `"ClaudeCode": found on PATH.` where the console showed `ClaudeCode`. Only `l` (literal) belongs
on the message. And `{Properties:j}` renders `{}` on every line that has no scope, which is most of
them — which is why `ActLogFormatter` exists instead of an output template: it writes the context
group only when there is context. It composes two `MessageTemplateTextFormatter`s around that group
rather than re-implementing rendering, and it derives the group by subtracting the message
template's own property names, so a scope key added later needs no change here.

**A `null` property renders as the word `null`, and `"l"` throws on a non-string.**
`ScalarValue.ToString("l", null)` is a `FormatException` for an `int` (`l` is only meaningful for
Serilog's string scalars) — and the file sink swallows formatter exceptions, so the symptom was a
line that simply stopped mid-write. Hence the string special-case in `WriteValue`. The `null`
rendering is why `QueueRunner.Report` switches on the hold reason instead of using one template:
`ReadyHold`'s fields are null or zero for every hold that says nothing about them, so a single line
would read `until null, 0 of 0 slots in use` most of the time.

### The log file is UTF-8 **with** a BOM

Serilog's file sink defaults to UTF-8 without one, and the content is correct either way. The BOM is
for the readers: without it every Windows tool that falls back to the ANSI codepage — PowerShell
5.1's `Get-Content`, older editors — turns non-ASCII into mojibake, and non-ASCII does reach this
file. `QueueRunner` logs the sentence it showed the user, which is localised, and a Windows profile
name can carry accents. It cost one parameter.

### Four layers catch an unhandled exception — verified

Worth writing down because only two of them are ACT's code, and the reflex on reading
`StartupLog` is to assume the other two are missing.

| Escapes from | Caught by | Level |
|---|---|---|
| Any thread, nothing above it | `AppDomain.CurrentDomain.UnhandledException` (`StartupLog`) | `Critical` |
| A `Task` nobody awaited | `TaskScheduler.UnobservedTaskException` (`StartupLog`) | `Error` |
| An HTTP request | ASP.NET's own middleware — `DeveloperExceptionPageMiddleware` in Development, `ExceptionHandlerMiddleware` in Production | `Error` |
| A Blazor component or event handler | `RemoteRenderer` then `CircuitHost` | `Warning` then `Error` |

The bottom two are ASP.NET's own loggers, which reach the file because they are ordinary
`ILogger` categories — and they survive the `Microsoft.AspNetCore: Warning` floor in
`appsettings.json` because they log at `Warning` and `Error`. **Raising that floor to
`Error` would silence the renderer's line** (the one that names the failing component)
while leaving the circuit's; raising it past `Error` would lose every unhandled exception
in the UI, which is most of them in a Blazor app.

All four were triggered on purpose rather than reasoned about: a throwing minimal-API route
in both environments, and a throwing button handler on the settings page. Each produced a
full stack trace, and ASP.NET's request scope (`TraceId`, `RequestPath`, `ConnectionId`)
renders in the context group for free — `ActLogFormatter` names no keys of its own, so any
scope the framework pushes comes through.

**Crash telemetry rides on this table, and learned it the hard way.**
`TelemetryPump` originally subscribed to the top two rows directly, which looks like the
obvious way to catch a crash and misses **most of them**: a throwing button handler is row
four, and Blazor's renderer swallows it into an `ILogger` without ever raising
`AppDomain.UnhandledException`. The symptom was `app_started` arriving and `app_error` never
doing so, from an app that was throwing on purpose. `TelemetryErrorBridge` is an
`ILoggerProvider` instead — the one place all four rows converge — and the two direct
subscriptions are gone, because `StartupLog` already logs those rows with the exception
attached and keeping both would have reported them twice.

Its rule is **any record at `Error` or above that carries an `Exception`**, which is
deliberately wider than "a crash": a failed launch in `SessionLauncher` qualifies, and a
maintainer wants to know that agent spawns fail on some installs. Records without an
exception are ignored, because a message is a sentence and a sentence is the thing that must
not leave.

**It is not the telemetry-over-logging shape that was rejected** in *Telemetry is not a log*.
That objection was that every call site becomes a payload; here nothing of the record is read
except `Exception` — never the message, the state, or the formatter — so the localised
sentences and working directories that make this log useful cannot reach the wire.
`The_log_message_never_reaches_the_payload` pins it, and it was also confirmed end-to-end by
driving a real `CircuitHost` category through a real `ILoggerFactory` with a path in the
message: what came out was the type, `Program.&lt;Main&gt;$ (Program.cs:23)`, and nothing else.

### The correlation scope is `Task` + `Session`, and it nests

The spec asks for the session id as a correlation id; `ActLogScope` adds the **card number** beside
it, because that is the identifier a user reads off a strip. Two consequences worth knowing:

- **The scope has to outlive the call, so the method has to be `async`.** `SessionLauncher.StartAsync`
  and `ResumeAsync` were expression-bodied and Task-returning; a `using var scope` in a method that
  returns a task disposes the scope *before* the work it names begins. They are `async` now for that
  reason and no other.
- **Nesting is unavoidable and harmless.** `SessionRestorer` scopes a card and then calls the
  launcher, which scopes it again. The provider adds each scope property only if absent, so the pair
  collapses to one — `A_repeated_scope_is_not_written_twice` pins it, because the alternative
  (`[Task=1031 Task=1031]`) is the sort of thing nobody notices until a log is unreadable.

### Which level a swallowed exception gets — settled

A review of every `catch` that logs and carries on found the levels had drifted, and the fix is a rule
rather than a sweep, because two of the sites that look wrong are right.

**The shipped level is `Information`** (`appsettings.json`), so **`Debug` means invisible on an
install.** That is the whole question: a swallowed exception logged at `Debug` is one nobody will ever
read. So `Debug` is correct *only* where the event is normal operation rather than an anomaly.

**`Warning`, not `Error`, when the user's work is unaffected.** `Error` in this codebase means work
failed — `SessionLauncher.FailedAsync`, a session that died. A lost metric, a timeline that will not
enrich, a usage percentage that cannot be read: none of those are the user's work failing, and
ranking them alongside a failed launch is how an error log stops being worth reading. `Warning` is
visible at the shipped default, which is the actual requirement. `AgentInstallDiscovery.Locate` was
already the precedent.

**Frequency is part of the level.** Two sites stayed at `Debug` for this reason and one gained a
guard:

- `TranscriptPump`'s **`IOException`** is the transient the loop is built around — the CLI holds the
  file open and a mid-write moment is expected — and it polls at **one second**. It is normal
  operation, and raising it would write 3,600 lines an hour per session against a 32 MB × 14-file
  retention.
- `TranscriptPump`'s **`UnauthorizedAccessException`** is the opposite: a permission or antivirus lock
  does not heal, and the symptom is a card whose token and context figures silently never fill in. It
  is `Warning`, but **only on the first occurrence per tail** — the flag rides the loop rather than the
  pump, because one tail runs per live session. The tail keeps polling after saying it, since a
  scanner's hold can still let go.
- `HttpUsageProbe`'s network failure and timeout stayed at `Debug` because **something else already
  reports them at `Information`**: `UsagePump` logs `"{Agent} usage is {Availability}"` guarded by
  `if (result.Availability != reported)`, so going offline writes one visible `Unreachable` line per
  transition — and the UI shows it too, and `UsageBackoff` lengthens the interval. The probe's line is
  the redundant detail (which exception, with its stack). Promoting it would add an entry every poll,
  per agent, for a condition already stated once.

---

## Telemetry

### Telemetry is not a log — rejected

The obvious way to send usage metrics from a codebase that already logs through
`Microsoft.Extensions.Logging` is a second `ILoggerProvider` beside the file sink. It genuinely
works and it genuinely cannot disturb the file: `ILoggerFactory` fans each record out to every
provider independently, each with its own level filter, so a cloud provider confined by
`AddFilter<T>` leaves `AddActFileLog` untouched. It was still rejected, for one reason that matters
and two that follow from it.

**Every `ILogger` call site would become a potential payload.** ACT's log deliberately carries the
things the switch promises never leave — prompts, working directories, the localised sentence the
queue showed the user, agent output. Confining that to one category is a *filter configuration*,
which means the promise on the Diagnostics page would be one `appsettings.json` line away from
being false, with nothing failing and nothing to notice. `ITelemetrySink` has no such surface:
nothing leaves that a call site did not hand over by name, and the set of things a call site *can*
hand over is a single file (`TelemetryEvents`).

The two that follow: a message template plus a `state` object is the wrong shape for `event` +
`distinct_id` + typed properties, and reconstructing one from the other is work the compiler cannot
check; and `ILogger` has no vocabulary for consent read at runtime or for a guaranteed final flush,
which provider disposal is the wrong hook for.

MEL is still involved, in one direction only: the sink logs *its own* failures to the disk log
through `ILogger<PostHogTelemetrySink>`, the same way `HttpUsageProbe` does. Never the reverse.

### What telemetry may carry

**First, how much of it there should be — trimmed.** The first cut shipped a
`task_completed` event carrying `turn_count`, `tool_calls`, `tokens_total`, `compactions`,
`transition_count` and a duration, and it was wrong in a way that is worth naming because it is the
easy mistake: those are measurements of **the agent's work**, not of ACT's use. They are also already
on the card, visible in the UI, and useful there. Sending them buys a solo maintainer nothing they
would act on, while making the payload something a user has to read carefully — which is exactly the
cost the opt-out is supposed to avoid paying.

The scope that survived is **the app and its settings, never a card's own progress**. `task_launched`
stayed, because which agent, and whether a schedule or attachments were involved, are facts
about which of ACT's *features* get used, not about how the work went.

**And it fires on a launch only, which is a volume decision as much as a scope one.** The first cut
reported every start with a `resumed` flag, and that inflates the count without adding a fact:
`SessionRestorer` replays one per live card on **every** startup, and a terminal restart is the same
session coming back into a fresh pty — so a card ACT restarted five times would read as five starts,
and the launches that actually happened would be buried under them. The guard is
`kind is StartKind.Launch`, and the flag is gone with the event that needed it. A **retry** still
counts, deliberately: the launcher's own model treats it as a launch — it claims the card and moves it
to Executing — so it is a start the user asked for rather than a re-attach ACT performed.
`SessionLaunchTelemetryTests` pins all four entry points.

**`agent_discovered` went the same day, for the volume reason rather than the scope one.** It fired
once per adapter on every startup, and its one useful fact — which agents this install actually uses —
is already in the settings snapshot as `enabled_agents`. What it added over that was on-`PATH` versus
an explicit path, which is not a question worth an event per adapter per run.

**`app_stopped` went too, and it is the plainest case of the three:** an uptime figure nobody was
going to act on. Shutdown still runs — it just flushes now, because a queued batch that never left
would take the run's events with it.

**And `settings_snapshot` was folded into `app_started` rather than dropped, which is a different
lesson: the meter counts events, not bytes.** PostHog bills ingested events, so sending the run in one
event and its settings in a second doubled what every startup cost to say exactly one thing. Merging
them also reads better in the tool — a run can be broken down by any setting without joining two
events together. It is worth carrying that the other way too: when a new fact is wanted, the first
question is which existing event it belongs *on*, not what to call the new one.

Three events is where this landed, and the direction is worth stating: **prefer removing an event to
adding one.** Every payload here is something a user has to be willing to send, and each one that
earns its place makes the switch easier to say yes to. The first cut had seven; four rounds of "do I
care about this one?" took it to three, and none of the answers were close.

`Nothing_reports_a_cards_own_progress` pins the removals by name, and
`Every_declared_property_is_actually_emitted_by_something` catches the litter a trim like this leaves
behind — a declared key nobody sends is a key a reviewer has to reason about for nothing. It has
already earned itself twice.

Then two belts, and the second exists because the first is a convention.

`TelemetryEvents` is the only place a payload is constructed — call sites pass typed arguments and
never a property bag — so the whole sendable surface is one reviewable file. Two of its factories
are handed a `Card` and a `UserSettings`, which is deliberate: seven loose arguments read worse and
drift faster than one object read narrowly. What comes off them is counts, flags and enum names.
Nothing else. A card holds the prompt, the title and the working directory; settings hold every
template's text and every agent's binary path.

`TelemetryPayload.Sanitize` is the belt under that. An event whose name is not declared never
leaves, a property whose key is not declared never leaves, and a value that is not a bounded scalar
never leaves — bool, `int`/`long`/`double`, an enum reduced to its name, a short symbol string, or a
capped list of them. The forbidden characters (`/ \ ~ " ' CR LF TAB`) are chosen for **what they
mark, not what they are**: a path separator, a home shortcut, a quote or a newline is what
distinguishes a leaked prompt or command line from the version strings and enum names this is meant
to carry. `Microsoft Windows NT 10.0.26200.0` survives; `C:\Users\…` does not.

Crash reports are held to the same rule and are the closest call in the set, because the natural
payload is exactly the one that leaks. `Exception.Message` is never read — it routinely names a
working directory or quotes a prompt — and `TelemetryFrames` asks `StackTrace` for
`fNeedFileInfo: false`, so a frame is `Type.Method` with no source path and no line number.

### The switch has two gates, because one is not enough

`ConsentedTelemetrySink` reads `UserSettings.Telemetry` on **every** capture rather than once at
startup. Read once, turning it back on would need a restart, and a user who opts back in should not
have to discover that. It is the same reason `UsagePump` re-checks `EnabledAgents` every pass.

That front gate alone would still have left a gap of up to one flush interval: it stops new events
being queued, but the client holds a batch of its own and its timer would deliver a batch queued
*before* the switch moved. So the vendor options also carry a `BeforeSend` hook wired to the same
predicate. PostHog drops an event whose `BeforeSend` returns null (verified against
`PostHogClient.CaptureBatchAsync`, 2.12.2 — hence the `null!`, which is the contract rather than an
oversight). Together they make "off" true of the queue as well as of the call sites.

### What a crash report may carry, and what the first one didn't — fixed

The first real `app_error` to arrive said `System.AggregateException`, `fatal: false`, and nothing
else. No frames at all. Two separate faults, and neither was the sanitizer being too strict.

**The wrapper was being reported as the failure.** `TaskScheduler.UnobservedTaskException` hands the
handler an `AggregateException`; the real exception is inside it, and the wrapper carries no stack of
its own — so `TelemetryFrames.Of` returned an empty array, `TelemetryPayload` dropped the empty list,
and the key vanished. Every distinct fault in the app would have grouped under one useless heading.
`TelemetryFault` now unwraps (`AggregateException.Flatten()`, then `InnerException`, capped at five
deep): `exception` is the **innermost** type, `exception_chain` shows the wrapper when there was one,
and frames come from the innermost exception that *has* any, falling outward.

**The frames had no file or line, and they safely can.** This is the distinction worth keeping
straight, because it is the one that decides what a crash report is allowed to say:

- `Exception.Message` is **runtime data from the user's machine**, and in this app it routinely names
  the file that failed. It never leaves. That is also why `CaptureException` cannot be used (below).
- A **source file name and line number are facts about ACT's own source**, resolved from the PDB that
  ships beside the binary. They describe this repository, not anything of the user's. The sentence on
  the switch is about *their* paths.

So a frame is now `Namespace.Type.Method (File.cs:42)`. Base name only, never `abs_path`: the absolute
path is the build machine's and adds nothing — and `IsSymbol` rejects separators, so a full path would
have taken the whole frame down with it rather than being trimmed. Two measurements behind this:
`Act.App.pdb` does ship in the packaged installer (`resources/bin/`, beside the DLL), and file and
line still resolve under a **Release** build, which is what
`A_frame_carries_the_source_file_and_line` pins by regex. Inlining can still merge or drop a frame;
that is a property of optimized builds, not of this code.

### A disconnected circuit is not a crash — measured

`app_error` arrived in PostHog in bursts of exactly **ten**, always from one user inside one minute,
always `System.AggregateException` → `Microsoft.JSInterop.JSDisconnectedException`, always
`fatal: false`, and never with anything visible on screen. Ten is not a coincidence: it is
`CrashReports.Most`. The burst was spending the whole per-run crash budget, so **every real crash
after it in that run went unsent** — that is the bug, not the noise.

**Where it comes from, measured rather than guessed.** A faulted task nobody awaited carries no
caller frames, so neither PostHog's frame list nor the log's stack said who started the call. A
temporary `AppDomain.CurrentDomain.FirstChanceException` probe logging `Environment.StackTrace` on
every `JSDisconnectedException` answered it in one run. Reproduction: open a card page, reload it,
and wait out Blazor's three-minute `DisconnectedCircuitRetentionPeriod`; `CircuitHost.DisposeAsync`
then disposes the orphaned circuit's components with the browser already gone. What the probe caught:

| Caller at throw time | Awaited? |
| --- | --- |
| `MainLayout.DisposeAsync` (and ACT's other module teardowns) | yes — `catch (JSDisconnectedException)` already handles them |
| `RadzenTooltip` / `RadzenContextMenu` / `RadzenChartTooltip` `.DisposeAsync` | yes |
| `RadzenDropDown.Dispose` → `Radzen.JSRuntimeExtensions.InvokeVoid` → `Radzen.JSRuntimeExtensions.FireAndForget(ValueTask)` | **no** |

Radzen's `FireAndForget` starts an interop call on a circuit that is already being disposed and drops
the task. The GC finalizes it, `TaskScheduler.UnobservedTaskException` raises it wrapped in an
`AggregateException`, `StartupLog` logs it at `Error` with the exception attached, and
`TelemetryErrorBridge` — which by design reads every logged exception — turns it into `app_error`.
Nothing here is ACT's to fix at the call site, and by construction there is no user left to show
anything to: the exception *means* the browser is gone.

So `CrashReports` refuses it, keyed on `TelemetryFault.Failure` — the innermost exception, which is
the type the report would have been grouped by anyway. The refusal is placed **before** the counter
so a burst cannot spend the cap, which is the whole point. It is deliberately narrow: a `JSException`
is a real fault in one of ACT's own modules with a live circuit behind it, and still reports.

The local log still records every one of them. It is the wire that is filtered, not the file — a
`nobody awaiting it` line naming `JSDisconnectedException` in `act-*.log` is expected and benign.

### `CaptureException` is not usable here — measured

The SDK has a `CaptureException` that feeds PostHog's Error Tracking product — grouping, issues,
occurrence counts — and `app_error` looks exactly like the thing that should be using it. It cannot,
and the reason is worth writing down because the suggestion will come back.

Measured against 2.12.2 by intercepting the payload in `BeforeSend` rather than reading about it, an
`IOException` produced:

```json
"$exception_message": "Could not read C:\\Users\\kev\\private-project\\prompt.md",
"$exception_list": [{
  "value": "Could not read C:\\Users\\kev\\private-project\\prompt.md",
  "stacktrace": { "frames": [{
    "abs_path": "C:\\Dev\\north-lab-kev\\...\\TranscriptPump.cs",
    "lineno": 37,
    "context_line": "            throw new IOException(...);",
    "pre_context": [ "…five surrounding lines of real source…" ]
  }]}
}]
```

Three leaks, and they are not equally serious. `abs_path`, `lineno` and the source snippets come from
the **build** machine through the PDB — on an install those files do not exist, so the context comes
back empty and the paths describe this repo rather than the user's disk. The one that matters is
**`$exception_message`, which is runtime data from the user's machine**, and in this app it reliably
names a file: the `IOException` and `UnauthorizedAccessException` in `TranscriptPump` both carry the
path that failed. That is a flat contradiction of the sentence on the switch.

**Scrubbing it in `BeforeSend` was considered and rejected** — it means allowlisting inside a
vendor-defined nested structure that can change shape on any SDK bump, which is the same fails-open
trade as `ILogger`-as-transport above, and rejected for the same reason. A guarantee that silently
stops holding is worse than one that was never claimed.

**The route that stays open, if Error Tracking is ever wanted:** it ingests any event named
`$exception`, and `CaptureException` is only a convenience wrapper. Per PostHog's manual-installation
docs, `filename`, `lineno` and source context are **optional** — the required shape is `type`,
`value`, `mechanism`, and frames carrying `platform: "custom"`, `lang` and `function`, all of which
`TelemetryFrames` already produces. Renaming `app_error` to `$exception` with a hand-built
`$exception_list` (type as both `type` and `value`, no message, no paths) plus an explicit
`$exception_fingerprint` would land in Error Tracking with every byte still constructed in
`TelemetryEvents`. It was not done, because it trades a shape the SDK maintains for one we own and
must re-verify on every bump, and grouped crash reports are not worth that yet.

### The consent gate must not outlive its provider — learned from a crash

First real shutdown after telemetry went live threw `ObjectDisposedException: IServiceProvider` out of
`PostHogClient.ApplyBeforeSend`. The gate was written as `Func<IServiceProvider, bool>` and resolved
`UserSettingsService` **on every check**, which is fine until you notice where the check runs.

The order at shutdown, which is the whole lesson:

1. `ServiceProvider.Dispose()` begins, and the provider refuses further resolution *while* it is
   disposing — not after.
2. Singletons are disposed in reverse **instantiation** order. `TelemetryPump` is built first (from
   `Program`), and the client is built lazily on the first `Capture`, which happens *inside*
   `pump.Start()`. So the client is disposed first.
3. The client flushes its last batch as it disposes. That flush calls `BeforeSend`. The gate reaches
   for a provider that is already refusing, and the closing batch dies with the exception.

So the gate has to **hold** what it needs rather than go looking for it: the factory is now
`Func<IServiceProvider, Func<bool>>`, resolved once while the container is alive, returning a
predicate that closes over the instance. `UserSettingsService` holds nothing disposable, so a closure
outliving the provider is safe — asking the provider at that point is not.

Two things fell out of it worth keeping:

- **The gate fails closed.** It runs inside the client's batch send, where a throw both loses the
  batch and surfaces from a stack no call site owns. A consent check that cannot answer now means "do
  not send", never "send anyway" — and `A_gate_that_cannot_answer_drops_rather_than_throws` is written
  the buggy way on purpose, which is also what gives the two regression tests beside it their teeth.
- **The client's own dispose is what delivers the closing batch**, not `TelemetryPump.DisposeAsync`.
  By the ordering above the pump flushes a client that is already gone; measured, that call completes
  silently rather than throwing, so it costs nothing and logs nothing. It stays as insurance against
  an SDK that stops flushing on dispose — but it is not what is currently saving the events, and
  anyone reasoning about the shutdown path should know which of the two is load-bearing.

### The token is encoded, and that is not security

The project key reaches a release through a `POSTHOG_PROJECT_TOKEN` GitHub secret: the `gate` job
fails if it is empty or does not start with `phc_`, and the `package` job base64-encodes it into
`appsettings.json` before publishing. `TelemetryOptions` accepts either encoding, told apart by that
same prefix — `_` is not in the base64 alphabet, so a plain key can never be read as an encoded one,
and a local run can paste the real thing with nothing to configure.

**What the encoding buys is that the key is not sitting in clear in a shipped file. That is all it
buys, and the distinction has to survive in writing, because the next person to read the pipeline
will see an encoded credential and infer a secret store.** Base64 is an encoding. The value sits next
to a property named `ProjectToken`; anyone who wonders what it is has it back in seconds. It was
argued against on those grounds and adopted anyway as a deliberate call — the cost is one small
method and a prefix check, and the appearance of a key in an installer is a fair thing to not want.

So the rule that goes with it: **nothing that is genuinely secret may be put through this path on the
strength of it being encoded.** A PostHog project key is safe to ship because it is *write-only by
design* — PostHog publishes it in the script tag of every site that uses them — not because of the
base64. A personal API key (`phx_`) can read and delete, and must never reach a client in any
encoding; the `gate` check exists to catch exactly that paste. If the key is ever abused, the answer
is a spend cap and a rotation, both of which cost one value in the next release. That is the real
protection, and it is why keeping the key cheap and replaceable beats trying to keep it hidden.

Two things kept honest by tests rather than by intent: `What_the_pipeline_stamps_is_what_the_app_binds`
runs the pipeline's own encoding through the section the app binds, so a renamed section fails here
even though the pipeline's read-back would pass; and `Anything_that_is_not_a_project_key_leaves_it_
unconfigured` makes a malformed value mean *no client* rather than a client that fails every batch
silently at the far end.

### `PostHog`, not `PostHog.AspNetCore`

`PostHog.AspNetCore` 2.8.2 depends on `PostHog >= 2.12.2` plus `Microsoft.FeatureManagement`, and
everything it adds over the core package is feature-flags-in-a-web-request:
`IPostHogFeatureFlagContextProvider`, the `<feature />` tag helper, `FeatureGateAttribute`,
request-scoped flag caching. ACT is a single-user desktop shell with no per-request identity and no
feature flags, so that is a dependency for nothing — and the core package is what does the work in
either case: batching, retry, compression and the flush timer are all its.

Adding it later, if flags are ever wanted, is a `PackageReference` and one registration line.
Nothing else changes, because everything above the vendor is `ITelemetrySink`.

Two consequences of using the SDK at all, both handled in `Act.Infrastructure/Telemetry/`:
`AddPostHog()` would normally be called from the app's DI extensions, which is what
`Only_the_telemetry_folder_names_posthog` exists to prevent — the registration lives behind
`AddActTelemetryClient` instead, exactly as `AddActFileLog` hides its own vendor. And the SDK binds
its own `"PostHog"` configuration section, which ACT does not use: one `"Telemetry"` section owns
`Enabled`, the token, the host and the flush knobs, and `ActTelemetry` maps them onto
`PostHogOptions` explicitly. One section named for what it does beats two that half-overlap.

**`Configured` is both halves.** A build needs the switch *and* a token before anything is wired;
without them the sink is `NullTelemetrySink` and no client exists at all. That is what makes a fresh
clone and the whole test suite silent by construction rather than by anyone remembering to turn
something off — and `appsettings.Development.json` sets `Enabled: false`, so a dev run and an agent's
sandbox never reach the project either.

---

## The store

### The migration list was emptied for the first release

`ActSchema.Migrations` held two entries, both written against stores that only ever
existed before the first release: `TaskDefaults` → `Templates` (schema 1 → 2, a field becoming a
list) and `NullFieldsBecomeDefaults` (schema 2 → 3, dropping the nulls LiteDB's `EmptyStringToNull`
default had written in place of empty strings). Both are gone, and **schema 1 is the release
baseline**: everyone starts on a store this build created, so there is no older shape for a
migration to find and nothing the two entries could do but run against a store that cannot exist.

**The machinery stays, only the list is empty.** The version document, the ordered list,
`CurrentVersion` derived from its length, the newer-build guard and the mapper handed to every
entry are all still there — the next breaking change is one array entry, not a rebuild of the
mechanism. The rules the two entries established are kept in `docs/repository-structure.md` rather
than in the code they came from, because they are what the *next* entry has to obey:

- **Write through the mapper, never by hand.** LiteDB's entity mapper serializes an `Id` property
  as **`_id` even on a nested object**, so a hand-built `["Id"] = …` round-trips as `Guid.Empty`;
  an enum's on-disk encoding is likewise the mapper's choice and not a fact you can assume. Read
  the old document into the new type and write it back with `ToDocument`. `SettingsStoreTests`
  pins that id round-trip, because nothing in the type declaration says it.
- **Be a no-op on a document you do not recognise.** A fresh store has no settings document at
  all, so an invariant cannot be *established* by a migration; `UserSettingsService.Seeded` owns
  the default-template and install-id invariants for exactly that reason.

**The cost was paid in stores, and it is the last time.** Emptying the list takes `CurrentVersion`
from 3 back to 1, so every store already stamped 2 or 3 trips the newer-build guard and has to be
deleted — acceptable exactly once, under the pre-release rule, and never again after the release
that makes schema 1 real.

**`InstallId` is seeded, and that is not the store rule being bent.** The
telemetry `distinct_id` looked like it wanted a migration entry, and it does not. A migration exists
to stop *two shapes* living on disk; an identity generated once when it is absent is a **seed**, not
a second shape — after `Seeded` runs there is exactly one shape and no code path that has to ask
whether the field is there. The same `Seeded` already owns the default-template invariant for
exactly this reason, so it is one more line in a place that is already tested rather than a schema
bump that would rewrite every install's settings document to add a GUID. It is random and never
derived from the machine: the switch promises anonymous data, and a hardware-derived id would tie
every install to a device.

### The git fields were dropped with the store, not with a migration

The git-when-done feature was deleted outright: `Card.AutoGit`, `TaskTemplate.GitAction` /
`Draft`, the `GitAction` enum, `AutoGitInstruction`, the four localised sentences, the form
dropdown, the `autoGit` MCP argument and the `auto_git` telemetry property. Its whole effect was to
append one sentence to the prompt at launch, which the user writes better in the prompt itself.

**Deleting the properties leaves the fields on disk**, because LiteDB's mapper skips a document
field with no matching property — every card and template written by an earlier build keeps its
`AutoGit` / `GitAction` / `Draft` keys, unread, with nothing to say when the last one is gone. The
answer was the pre-release one and it was taken deliberately: **the store was deleted**, not
migrated. Nobody outside this machine runs ACT yet, so there is exactly one store holding the old
shape and its owner chose to drop it. `ActSchema.Migrations` stays empty and schema 1 stays the
release baseline. After the release, this same change would have to be an entry in that list —
that is the whole point of the rule, and this is the last shape that gets to be fixed by deletion.

### Renaming a persisted enum value is never only a rename — measured

LiteDB persists enums by name and its deserializer throws on a name the build no longer has —
measured, not assumed: `ArgumentException: Requested value 'Spacious' was not found`. Where the
value loads decides the blast radius: a retired `Badge` costs one unreadable card, but settings
load in a constructor at start-up, so a retired `BoardDensity` name took the **whole app** down.
Before the first public release the answer is a fresh store; after it, a rename needs a schema
migration in `ActSchema` or a tolerant `RegisterType` in `ActBsonMapper` — in the same commit as
the rename, every time.

### Empty strings were stored as null — measured

`BsonMapper.EmptyStringToNull` **defaults to `true`** in LiteDB 5. Every empty string ACT ever
wrote went to disk as BSON null and came back as `null`, so a property declared
`public string Name { get; set; } = string.Empty;` was non-null in a fresh object and null after a
round trip. Nothing in the type says so, and nothing had dereferenced one until
`UserSettingsService.Seeded` reached for `template.Name.Length` — which crashed the app on the
**second** start of *any* store, fresh ones included. The first start had no settings document to
read; the first start's own write is what broke the second.

`ActBsonMapper` sets the flag to `false`, and `SettingsStoreTests` pins the round trip — an empty
string comes back empty, and a `string?` still comes back null — because nothing in the type
declaration says which one you get. A `NullFieldsBecomeDefaults` entry (schema 2 → 3) repaired the
stores the old default had already written; it went with the rest of the list, since
no store the release will see was ever written by a build that had the flag wrong.

**Had that repair been needed after the release, it would still delete the null field rather than
write `""` over it, and the two are not interchangeable.** Writing `""` everywhere would need to
know, per property, whether the type declares it nullable — `Model` and `Effort` are `string?` and
mean something by being null, while `Name` and `Title` are not — which is `NullabilityInfoContext`
reflection over every stored type. Removing the field instead leaves deserialization to skip it, so
the property keeps **its own initializer**: `string.Empty` where the declaration is non-nullable,
`null` where it is nullable. The type stays the single source of truth and the migration needs no
per-type knowledge, which is also what lets it walk every collection generically rather than
naming one.

---

## Styling against Radzen

Two bugs found while making the New task picker presentable. Both had been in the app
since the shell was built, both were invisible in code review, and both were found the same way:
reading `getComputedStyle` off the real page rather than trusting the CSS to mean what it says.

### The whole app was set in Times New Roman

`html`, `body` and even `.act-root` computed `font-family: "Times New Roman"`. Radzen sets its
typeface on its **own components** and never on the document, so everything ACT styles with a plain
class of its own inherited the browser default. That covered every page's title in its top bar and
every title in a row-list — the archive and the templates page — and the terminal was unaffected only
because xterm sets its own font.

It survived this long because Radzen's widgets hid it. A label beside a control is a `RadzenText`, so
the two sat side by side in different families and read as a weight difference rather than a bug; the
mono readouts have `--act-mono` explicitly and looked correct. The fix is one line beside the `color`
anchor that was already there for the same class of reason — `font-family: var(--rz-text-font-family)`
on `html, body` — which also settles every popup Radzen mounts outside `.act-root`.

### An outlined button's edge is a box-shadow, not a border

Every secondary button in the app was ringed in `#c9cacd`, near-white on a near-black board: the
board's chips, *Browse*, the archive's row actions, the New task button. The colour is
`--rz-base-300`, read straight off Radzen's raw ramp, and it is drawn as `box-shadow: inset 0 0 0 1px`
— the buttons' `border-width` is **`0`**.

That last detail is why it lasted. `BoardView.razor.css` had been setting `border-color:
var(--act-line2)` on the New task button since it was written; the declaration was valid, applied,
and painted nothing, so the CSS looked like it was already handling the case. Two further traps came
with it:

- **The theme names the shade.** Its rule is `.rz-button.rz-variant-outlined.rz-base.rz-shade-default`,
  so a three-class override loses on specificity and changes nothing. The same trick was needed for the
  button's `color`, which had also been silently losing — the `+` glyph was rendering at full
  `--act-ink` while the CSS asked for `--act-dim`.
- **Two of those rings meeting is a seam.** A split button is two buttons in a wrapper, so the pair
  drew two lines down the middle and hovering one lit half a box. The ring moves to the wrapper and
  the halves give theirs up.

Remapping `--rz-base-300` itself was rejected for the reason `app.css` already records for the
`--rz-base-*` ramp: the slot also draws the gauge scale, the timeline point and the upload widget,
none of which want a hairline.

### How to find the next one

`dotnet build` cannot see any of this, and neither can a screenshot at 1× on a dark theme. What found
both was **3× zoomed captures driven over CDP** — the in-app preview pane does not composite, so it
cannot screenshot at all (see the memory note on verifying xterm) — followed by `getComputedStyle` on
the specific element to name the colour, then a search for that literal among the `--rz-*` custom
properties to find who owns it. Enumerating `document.styleSheets` to find the *rule* is not worth
attempting: it failed three times here on selectors the CSSOM reports in a form `Element.matches`
rejects.

## Testing the app in a real browser

### How the browser suite gets a deterministic agent

`Act.App.E2eTests` runs the real app through `WebApplicationFactory<Program>` on a real Kestrel port and
replaces both `IAgentAdapter` registrations with `MockAgentAdapter` in `ConfigureTestServices`. It looks
like the heavyweight option; the two lighter ones were tried on paper first and both fail on something
specific.

**A fake CLI on disk, pointed at by `AgentDefaults.Binary`.** By far the most attractive: a real pty
running a real program, no test wiring inside the host, and it would cover `PtyHost` — which the mock
adapter cannot. It does not survive contact with the code. `PtyHost` hands
`ExecutableResolver.Resolve(...)` straight to the pty as `App`, and unlike `CommandHost` it has **no
`cmd /c` shim** (`CommandHost.cs` has one precisely because a `.cmd` cannot be started with
`UseShellExecute = false`; `PtyHost.cs` does not). So a `.cmd` or `.sh` fake will not spawn on Windows,
and pointing `Binary` at `node.exe` does not help either — the adapter owns the argument order, so the
script path cannot be made the first argument. Reviving this needs a per-platform native executable, or
a `cmd /c` shim in `PtyHost` that nothing else wants.

**A production switch** (`Act:UseMockAgent` or similar). Rejected on the rule that decides where every
other knob goes: `appsettings` holds facts about the machine and the vendor, the store holds the user's
choices, and a test hook is neither. It would also be a permanent seam in shipped code for the benefit
of one test project.

What the chosen route costs, and why the fixture looks the way it does:

- **Two hosts run, not one.** `WebApplicationFactory` casts whatever `CreateHost` returns to a
  `TestServer`, so a second host has to exist even though nothing talks to it — and it boots ACT
  completely. LiteDB takes an exclusive lock on `act.db`, so the two cannot share a data directory; the
  shadow gets a store nobody reads. The environment is re-read on each `Build()` (the resolver re-runs
  `Program` from the top), which is what makes setting the directory between the two builds work.
- **`UseStaticWebAssets()` is not optional.** Without it every asset 500s: `MapStaticAssets` resolves
  files through the web-root provider, the web root is the test project's folder, and the manifest that
  points back at the real files is only loaded automatically in Development.
- **The log providers are cleared.** Not for quiet: `StartupLog` watches for unobserved task exceptions
  and logs them, and on teardown that handler runs on the finalizer thread *after* the providers are
  disposed. The Windows event log provider then throws `ObjectDisposedException` out of a finalizer and
  takes the whole test host process down. It did, twice.
- **Configuration arrives as environment variables**, not `UseSetting`. `Program.cs` reads the data
  directory out of `builder.Configuration` on its second line, before any `ConfigureWebHost` callback has
  run. That is also why the assembly disables test parallelisation.

### Knowing a Blazor page is ready to be clicked — the one-in-six flake

`GoAsync` waits for three things before a spec touches the page, and the third is the only one that
answers the question that matters. The history is worth keeping, because two earlier gates each looked
sufficient and were not, and the failure they let through was invisible in the worst way: **the click
simply vanished.**

The symptom was `SignOffTests.Confirming_empties_it` failing about **one run in six** — click the
archive button, wait for the board, board never comes. Everything about it pointed the wrong way. It
looked like slowness, so the expect timeout went from 5s to 15s and the failure rate did not move. It
looked like a render fault, because Playwright's aria snapshot showed the layout header present and
`main` empty. It looked like it might be the new MCP server slowing startup, so it was measured against
a worktree at the previous commit — which flaked at the same rate, clearing that.

What settled it was instrumenting the failure instead of theorising about it: at the moment the wait
expired, the card was **still on the board** and the page was **still on `/edit`**. Nothing had been
asked of the server at all. Clicking a second time always worked.

So the click was landing on markup that had no handler behind it. Blazor Server prerenders the page as
plain HTML, and until the circuit adopts it there is nothing to receive an event — a click in that
window is discarded silently, with no error anywhere. `GoAsync` was waiting for the `_blazor`
WebSocket, which proves only that the server is reachable.

The gate that finally holds is **`_bl_<guid>` attributes**: Blazor stamps one on every element it has
bound a handler to, and only when the circuit renders. Measured rather than assumed — the prerendered
HTML straight off the socket carries **zero**, the same page once attached carries **62**.

One weaker gate was tried in between and is worth naming so nobody re-derives it: waiting for Blazor's
`<!--Blazor:…-->` prerender comment markers to disappear. It is *nearly* right — the markers do go on
attach — but they go when the circuit begins adopting the component, before handlers exist. It still
flaked three runs in fourteen.

Rates, all on the same machine, same suite: no gate **1 in 6**, marker gate **3 in 14**, handler gate
**0 in 16**, then the full suite twice more green.

### Synthesising a paste

Two of the `act-attach` specs build a hand-made clipboard object rather than a real `DataTransfer`, and
that is deliberate twice over. Chromium ignores `clipboardData` in `ClipboardEvent`'s init dictionary, and
files re-hosted through a page-built `DataTransfer` never reach Blazor's chunked upload — measured, not
assumed. More to the point, the rules being pinned cannot be expressed with a real one: `items.add()`
populates `files` too, so `files` and `items` can never be made to *disagree*, which is the whole subject
of the "a screenshot tool put the picture on the clipboard twice" and "yield to the text" cases. The
hand-made shape is what Windows actually produces.

### One scratch folder for every "no folder" task, not one each

A task with nowhere in particular to run needed a folder anyway, because both CLIs read whatever they
start in and a directory that does not exist fails at spawn. The obvious shape — a fresh directory per
card, which also sidesteps the one-task-per-folder guard for free — was **rejected on a measurement
already in this repo**: a pty in a directory the CLI has not seen hangs on the directory-trust prompt
(`hasTrustDialogAccepted` in `~/.claude.json`, see `docs/findings/agent-usage.md`). Per-card folders
would therefore park every quick question at a prompt nobody is waiting on. One shared folder is
trusted once and never asks again.

**The scratch directory itself was already there**, used by the title queries, and that precedent
transfers less far than it looks: those runs are `-p --tools ""` and `codex exec --sandbox read-only`,
so they have no TUI to draw a dialog in and nothing to write. What they prove is that both CLIs are
content to start somewhere that is not a git repo — not anything about trust for an interactive session.

The exemption from the folder guard is keyed on `Card.NoWorkingDir` rather than on the path being blank,
and it is read on **both** sides of the comparison. The asking side is the obvious one; the holding side
matters because a no-folder card holds the scratch directory and nothing else, so it cannot be standing
in the way of a card that wants a real folder. In practice `WorkingDir` is blank on one of these and the
resolve would answer null anyway — the flag is what *states* it, so a card carrying a stale path cannot
block on the strength of it. The queue runner's `SameFolder` needs the same exemption for the same
reason, one layer down where nothing on screen would explain the wait.

`NoWorkingDir` needed no migration: an absent boolean reads as `false`, which is exactly right for every
card that predates it. `TaskTemplate` carries the same flag, so a user can save their own starting point
for questions.

**A built-in "Quick question" template was written and then removed**, and the reasons it lost are worth
keeping, because it is the obvious next idea. It bought one click — the caret on the board's New task
button — and cost: a second undeletable template with a name nobody chose, a row on the templates page
for every user whether they wanted it or not, a second `IsDefault`-shaped flag on `TaskTemplate` (a third
built-in would have forced that pair into a persisted `TaskTemplateKind`, and a migration with it), and
the New task control becoming a split button on every install, since `PickableTemplates` would never be
empty again. The switch on the form is the whole feature; the template was packaging.

## Releasing

### A downgraded ACT refuses the store out loud, and the refusal has to ship *first*

`ActSchema` already threw on a store from a newer build. The problem was where the throw landed: the
store opens on the first service that needs it, which is several lines into `Program`, and under Electron
that is long before the ready callback — so there was no window, no bridge, and no way to say anything.
A downgraded ACT died behind its own splash screen, the same failure mode `ElectronUpdater.Configure`
once had.

So compatibility is now a **pre-flight question**, asked by `ActStoreCompatibility.Inspect` with no
container and no migration: open, read one integer, close. The file is checked for existence first
rather than opened blindly, because `new LiteDatabase` *creates* it — a probe that opened
unconditionally would leave an empty `act.db` on every first run and hand the real open a store to
migrate from nothing.

An unsupported answer skips every line that would open the store and, under Electron, starts the host
only as far as the ready callback, where `Electron.Dialog.ShowErrorBox` is drawn. `ShowErrorBox` rather
than `ShowMessageBoxAsync` because it is the one Electron dialog that needs no parent window — and here
there is no window and never will be. Browser mode has no dialog surface at all, so a critical log line
and a non-zero exit are the whole report. The message names both numbers and says the board is intact,
because the failure is a refusal and not damage.

The dialog's language is the **OS's**, not the user's: the preference lives in the store that cannot be
read, so `ApplyLanguage` never runs and the resources resolve against `CurrentUICulture`. That is the
closest thing to right that is still knowable.

**This only ever helps a downgrade to a build that already contains it.** The code has to live in the
*older* build — the one being installed — so v0.9.1 through v0.9.3-beta will still fail the old silent
way when they meet a schema-2 store. From this release forward, every downgrade is clean. There is no
way to fix the already-published builds, which is the argument for landing this in the same release as
the first migration rather than after the first complaint.

### The three-way update policy became a switch — schema 2

`UpdatePolicy` held `NotifyAndDownload`, `NotifyOnly` and `Off`, and its own comment defended the middle
one: "check but leave the download to me" is a real position, because a metered connection makes an
unasked hundred megabytes a real cost. It was collapsed to the `AutoUpdate` boolean anyway, and the
argument that beat it is that **the position survives without the choice**. Switch off, press *Check
now* when you want, press *Download* for what it found — that is the same bargain, driven by hand. What
is genuinely lost is being told about a version automatically without fetching it, and that is narrower
than a third position deserves. Two of the three only ever differed in what they did with the answer.

Retiring it took a real migration, not a tolerant read. LiteDB persists enums **by name** and its
deserializer throws on a name the build no longer has, and `UserSettings` loads in a constructor at
start-up — so a store still holding `"Updates": "NotifyOnly"` would not have cost a preference, it would
have been an app that does not open. `ActSchema.Migrations` gained its first entry since the list was
emptied for the first release, rewriting the stored document and removing the old field outright, so
only one shape exists on disk afterwards.

**Only `NotifyAndDownload` maps to `true`.** It is the one position that fetched a version on its own;
mapping `NotifyOnly` to `true` would start downloading behind the back of the single user who had
explicitly refused exactly that. A document with no `Updates` field predates the setting and takes the
model's default, which is why the migration tests cover the absent case as well as the three names —
missing must not read as `false`.

The telemetry property was renamed with it (`updates` → `auto_update`) rather than reused: a key whose
type silently changes from a string to a boolean is worse for whoever reads the dashboard than a new
key beside the old one.

### A silent install cannot tell you it finished — and the visible one is a wizard

Install-on-exit worked and was still a bad experience: ACT went quiet, nothing said when the installer
had finished, and launching from the taskbar too early hit a *shortcut not found* dialog — NSIS
rewrites the shortcuts partway through, so an early click resolves one that momentarily is not there.

The obvious fix is to show the installer, and it was rejected on a measurement. `QuitAndInstall`'s
`isSilent` is the only lever, and electron-updater turns it into NSIS's `/S` or nothing at all —
`NsisUpdater.doInstall` builds `["--updated"]` and pushes `/S` only when silent, so there is no middle
setting. ACT's installer is `"oneClick": false` with `allowToChangeInstallationDirectory`, which makes
the visible form the **assisted wizard**: Next, install directory, Install, Finish. On the way out that
waits on clicks from somebody who has already walked away, and closing the wizard cancels the update.

**`oneClick` cannot be silent-and-one-click, visible-and-assisted.** It is an electron-builder build
option that selects which NSIS script gets compiled into the installer, so there is one binary with one
mode; `/S` only chooses whether that mode is quiet. Getting both would mean shipping two installers per
release and pointing `latest.yml` at the one-click one — two artifacts to keep in lockstep, an update
that installs under different rules than the download the user chose, and twice the signing surface.
Not worth it.

So the real fix was not to decorate the exit path but to stop making it the only route. *Restart and
install* is offered wherever a downloaded update is visible, installs silently with
`isForceRunAfter: true`, and lets **the app reappearing be the completion signal** — the one signal an
update can give, and better than a progress bar, because there is nothing to watch and nothing to click
early.

Install-on-exit was kept underneath at first, for people who simply leave. **It is gone now, and the
button is the only route.** Left in, everything above was decoration: the tray's *Exit* installed, the
last window closing with close-to-tray off installed, and `AutoInstallOnAppQuit` covered whatever else
got out — so a user who never clicked *Restart and install* was upgraded anyway, at the one moment ACT
had no way to tell them it was happening. That is exactly the silent, unreportable install this section
set out to stop offering; keeping it as the default made the visible route the exception. So
`AutoInstallOnAppQuit` is `false`, `ExitAsync` is `Exit(0)` whatever is on disk, and `InstallAndExit`
was deleted rather than left unreferenced.

**Nothing is lost by not installing** — read from electron-updater's source, not yet watched happen.
`executeDownload` asks `DownloadedUpdateHelper.validateDownloadedPath` about its pending cache before
fetching anything and emits `update-downloaded` either way, so a run that never clicks the button should
leave the file where the next run finds it, and the following `DownloadAsync` should resolve against the
cache rather than the network. Nothing was added on the strength of that: the code change here is a
deletion, and if the cache were missed the only cost would be a second download of a file ACT already
had. What would settle it is one release cycle — decline the update, restart, and watch whether the
settings page reaches *Ready* immediately or crawls back up through the percentages.

### A downloaded update may not be stale, and may not be checked for at the click

Once nothing installs on the way out, a downloaded update can sit unanswered for days — and the pump used
to stop checking the moment one was ready, on the reasoning that the answer could not improve until the
app restarted into it. With install-on-exit gone that reasoning fails: 1.3.0 ships, ACT goes on offering
1.2.0, and the user needs **two restarts** to arrive at the newest version.

The obvious fix — check when *Restart and install* is clicked, and fetch the newer version instead — was
rejected on the pinned electron-updater's source (6.8.9). `getValidCachedUpdateFile` compares the feed's
`sha512` against the cached one and, on a mismatch, calls `cleanCacheDirForPendingUpdate()` **before**
returning null, so the installer ACT was holding is deleted at the *start* of the replacement download,
not on its success. A click that re-checks therefore trades a certain install for a maybe: if the new
download fails — dropped connection, the 30-minute limit, a 404 — there is nothing left to install, and
`downloadedUpdateHelper.file` still names the deleted file, so a second click reaches `install`'s error
path and does nothing at all. The click would also have to wait out a check plus a full download, up to
32 minutes, under a button that says only *Restart and install*.

So the checking stays in the background and **the offer only ever moves to a version that is already on
disk**:

- `UpdatePump.PassAsync` keeps checking while `Ready`, and returns without touching the state when the
  answer names the version it is already holding — including *Checking*, because a button that blinked
  out every six hours would be worse than one that never noticed 1.3.0.
- A newer version publishes `Available` then `Downloading`, which un-offers the button by itself: both
  call sites render it only on `Stage is Ready`. That window is honest rather than unfortunate — the
  older installer really is gone by then.
- `IUpdater.DownloadAsync` takes the wanted version, and `IsReady` stopped being a latch. Asked for the
  version it holds it answers immediately; asked for a newer one it clears `IsReady` **before** fetching,
  for the same reason the cache is already empty by then. Without this the old early-out on a bare
  `IsReady` would have returned `true` and the replacement download would never have happened.
- No answer withdraws a downloaded version — not a failed check, not `UpToDate` after a release is
  pulled, not the toggle going off. The last of those was a real bug: switching off published `Idle` over
  `Ready` and took the button with it.

What is genuinely lost: with the toggle off, a *Check now* that turns up a newer version replaces the
*Restart and install* for what is on disk with a *Download* for what is not. `UpdateStatus` holds one
version, and modelling "1.2.0 ready, 1.3.0 available" was not worth a second one — the alternative was a
manual check that visibly did nothing, which is the dead button this section keeps arguing against.

It hangs off `IDesktopBridge` rather than `IUpdater`: a restart is an exit that comes back, so it has to
end the live sessions and put the running-work question through the same confirmation as the tray's
*Exit*, and none of that is the updater's business. `ElectronDesktopBridge` may take `DesktopShell`
without closing the container's loop, because the shell takes no bridge — unlike
`INotifier -> DesktopShell -> UpdatePump -> INotifier`, which is why the pump is registered where it is.

### The release tag cannot be pushed with git — measured

Cutting `v0.9.3-beta` failed on the tag push, not on anything ACT builds:

```
! [remote rejected] v0.9.3-beta -> v0.9.3-beta (refusing to allow a GitHub App to
create or update workflow `.github/workflows/release.yml` without `workflows` permission)
```

`GITHUB_TOKEN` is a GitHub App installation token, and GitHub's pre-receive hook refuses a push from
an App that it reads as creating or updating anything under `.github/workflows/`. A tag push trips
it: the run was dispatched from `main` and the tag pointed at `main`'s own tip, so nothing was being
changed, but a newly created ref has no previous value to diff the workflow files against. The
permission the message asks for **cannot be granted** — `workflows` is not a key the `permissions:`
block accepts, and no repository or organisation setting adds it to `GITHUB_TOKEN`. Nothing was
misconfigured; a settings-level mistake fails differently (a read-only token gives a plain `403`, a
protected tag says so by name). It is a live GitHub complaint, unanswered in
[community #151442](https://github.com/orgs/community/discussions/151442) as of January 2026.

So the tag is created through the API, which never reaches that hook — confirmed working by someone
hitting the same wall in [community #26164](https://github.com/orgs/community/discussions/26164), and
then by cutting `v0.9.3-beta` for real. **Two calls, not one**, and the second is the point:

- `POST git/tags` makes the annotated tag **object**, holding the message and the tagger date.
- `POST git/refs` points `refs/tags/<tag>` at that object rather than at the commit.

Letting `gh release create` mint the tag on its own is the more common shape in public workflows and
was rejected: it creates a **lightweight** tag, which has no object, so GitHub's Tags page falls back
to the tagged *commit's* date. ACT cut `v0.9.1` and `v0.9.2` off the same commit a day apart — as
lightweight tags both would have displayed the same timestamp. Dropping the step entirely would also
cost `--verify-tag`, one of the two independent guards against cutting a version twice (the other is
the gate's `git tag --list`, which sees API-created tags like any other ref).

`contents: write` alone is enough for both calls — **verified**, against one report that creating a
tag ref also needs `actions: write`. That extra permission was briefly added defensively and then
removed unused; the real run settled it. Do not add it back on a hunch.

`shell: pwsh` reports a native command's failure only at the *end* of a step, so the first call's
result is checked explicitly — without that guard a rejected tag-object call would fall through and
create the ref against an empty sha.

The shape of the step is pinned by `ReleaseTagCreationTests`, because nothing compiles against the
workflow and a release is manual, slow and public: a revert to `git push` would surface only as a
failed release after both platforms had already built, and a slip to a lightweight tag only as wrong
dates on a page nobody re-reads.
