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

### `codex --image` must bind its value with `=` — measured 2026-08-04

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

### Attachments travel as paths, never as contents — decided 2026-08-04

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

### A pasted screenshot may not be in `clipboardData.files` — measured 2026-08-04

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

### The hover thumbnail has to be `position: fixed` — measured 2026-08-05

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

Send-back (deleted 2026-08-01) composed a message and pressed the agent's submit key, which put ACT
in the business of guessing when a TUI was ready to be typed into. A file dropped on the Terminal tab
does neither: it inserts **the file's own path**, quoted, where the cursor already is, and sends
nothing. Every terminal emulator does this with a dragged file, and the user still has to read what
landed and press Enter. `IAgentTerminal`'s doc comment was amended rather than quietly contradicted —
the invariant is that ACT composes no *instruction*, not that it never writes.

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

---

## Logging

### Four traps between MEL and Serilog — measured 2026-08-04

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

### The log file is UTF-8 **with** a BOM — decided 2026-08-04

Serilog's file sink defaults to UTF-8 without one, and the content is correct either way. The BOM is
for the readers: without it every Windows tool that falls back to the ANSI codepage — PowerShell
5.1's `Get-Content`, older editors — turns non-ASCII into mojibake, and non-ASCII does reach this
file. `QueueRunner` logs the sentence it showed the user, which is localised, and a Windows profile
name can carry accents. It cost one parameter.

### Four layers catch an unhandled exception — verified 2026-08-04

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

---

## The store

### The first migration: `TaskDefaults` → `Templates` — 2026-08-04

Templates replaced the single *New task defaults* block, which is a breaking store change: a
field became a list, and one document's sub-object became that list's first entry. Two things
about how it is written are worth keeping, because neither is visible from the code.

**It is a migration, not a fresh `act.db`.** The pre-release rule still allowed wiping the store,
and the reason it was not used is that this change is entirely mechanical — the old document has
every value the new one needs, under the same names — so the only thing wiping would have cost is
the user's board, for nothing.

**It writes through the mapper, and the reason is `_id`.** The first draft hand-built the
template's `BsonDocument` key by key, which is wrong twice over: LiteDB's entity mapper treats an
`Id` property as the id field and serializes it as **`_id` even on a nested object** (so
`["Id"] = …` round-trips as `Guid.Empty`, giving every migrated install a default template the
New-task picker can name but never resolve), and an enum's on-disk encoding is the mapper's choice
rather than a fact you can assume. So `ActSchema.Apply` now takes the `BsonMapper` that
`ActDatabase` opened the database with, and the migration deserializes the old sub-document
straight into `TaskTemplate` — their fields overlap by name — then writes it back with
`ToDocument`. Nothing in either type's declaration says any of this, which is why
`SettingsStoreTests` pins the id round-trip rather than trusting it.

**The invariant is seeded outside the migration.** Every reader assumes exactly one template is
the default, and a migration cannot promise that: a fresh install has no settings document to
rewrite. `UserSettingsService` seeds it while loading, before anything can read a settings object
without one, so the migration is free to be a no-op on a document it does not recognise.

### Adding `TaskTemplate.Title` needed no schema bump — 2026-08-04

Templates gained a `title` the day after they landed, which normally means a second migration: the
documents schema 2 wrote have no such key, and *two shapes on disk* is the thing the store rule
exists to forbid. It did not, for two reasons worth separating.

**The migration writes through the mapper, so its output tracks the type.** `1 → 2` serializes a
real `TaskTemplate` with `ToDocument`, so the moment the property existed the migration started
emitting it — no edit, and no store that has run the migration is missing the key. A hand-built
`BsonDocument` would have needed a `2 → 3` to catch up, which is the second time that choice paid
for itself in as many days.

**No real store was at schema 2 yet.** Only this session's scratch stores had run it, so nothing
was stranded. Had the user's board been at 2, the honest answer would have been a `2 → 3` entry
stamping the key — *not* leaning on LiteDB filling the property from its initializer, which is a
read-time shim wearing a default value's clothes.

### Empty strings were stored as null — measured 2026-08-04

`BsonMapper.EmptyStringToNull` **defaults to `true`** in LiteDB 5. Every empty string ACT ever
wrote went to disk as BSON null and came back as `null`, so a property declared
`public string Name { get; set; } = string.Empty;` was non-null in a fresh object and null after a
round trip. Nothing in the type says so, and nothing had dereferenced one until
`UserSettingsService.Seeded` reached for `template.Name.Length` — which crashed the app on the
**second** start of *any* store, fresh ones included. The first start had no settings document to
read; the first start's own write is what broke the second.

`ActBsonMapper` now sets the flag to `false`, and `NullFieldsBecomeDefaults` (schema 2 → 3) repairs
what the old default wrote.

**The migration removes a null field instead of rewriting it, and the two are not
interchangeable.** Writing `""` over every null would need to know, per property, whether the type
declares it nullable — `Model` and `Effort` are `string?` and mean something by being null, while
`Name` and `Title` are not — which is `NullabilityInfoContext` reflection over every stored type.
Removing the field instead leaves deserialization to skip it, so the property keeps **its own
initializer**: `string.Empty` where the declaration is non-nullable, `null` where it is nullable.
The type stays the single source of truth and the migration needs no per-type knowledge, which is
also why it can walk every collection generically rather than naming one.

This is not the read-time shim the store rule forbids: the nulls are gone from disk after it runs,
and a build that later drops the migration still reads exactly one shape.

---

## Styling against Radzen

Two bugs found on 2026-08-04 while making the New task picker presentable. Both had been in the app
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
