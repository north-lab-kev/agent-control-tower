# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Project docs

- **`docs/overview.md` — MUST READ.** The index of the authoritative spec, which
  is split one file per area under `docs/spec/`: state model,
  data model, ingestion, rules engine, UI direction. Read it before any
  non-trivial change, and follow it when it disagrees with a mockup or with the
  existing code. Keep it updated when a decision changes.
- **`docs/repository-structure.md` — MUST READ.** Where everything lives and
  why: the project layout and the **dependency direction** (`Act.Core` depends on
  nothing infrastructural; adapters, infrastructure and UI point inward to its
  `Abstractions/` interfaces). Read it before adding a project, folder, or file so
  new code lands where the architecture expects it, and keep it updated when the
  layout changes.
- **`docs/design-notes.md` — read when a comment points at it, or before undoing something
  that looks arbitrary.** How the non-obvious decisions were reached: measurements against a
  pinned CLI, shapes that were tried and rejected, code that was deleted and why. The code
  states its invariants and cites this file rather than narrating the history inline, so a
  constant or a guard that looks removable is usually explained here.
- **`docs/findings/agent-usage.md` — read before touching usage code.** Where the
  5-hour and weekly numbers come from: the two undocumented HTTP endpoints, their
  response shapes, the unit and encoding traps between them, the rules ACT holds
  itself to around the CLIs' credential files, and the alternatives that were
  measured and rejected.
- **`docs/findings/agent-title.md` — read before touching one-shot queries or the
  auto-generated title.** The measured flag set for each CLI's non-interactive mode,
  the traps between them (stdin must be closed; Codex splits its streams; `claude
  --bare` silently breaks OAuth auth), and the checklist to re-test on a CLI bump.
- **`docs/findings/codex-hooks.md` — read before touching Codex hook wiring.**
  Codex hooks do not fire on the pinned CLI, so that code is written blind; the
  file records what was measured, what ACT assumed, and the checklist to re-test
  it against a newer CLI.

## Running the app

- **Do it yourself: check → stop → rebuild → run.** When you need to see the app,
  never ask me to launch it or to confirm my instance is stopped. Find whatever
  is already running, stop it, rebuild, start your own, and verify in the browser
  pane.
- Find and stop the current instance — the process holding the app port (5210),
  plus any Electron shell:

  ```powershell
  Get-NetTCPConnection -LocalPort 5210 -State Listen -ErrorAction SilentlyContinue |
      ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
  Get-Process Act.App, electron -ErrorAction SilentlyContinue | Stop-Process -Force
  ```

  Also check `preview_list` / `preview_stop` for a server an earlier session left
  running.
- Start it with `preview_start` on the **`Act.App`** configuration in
  `.claude/launch.json` (http profile, <http://localhost:5210>). For the Electron
  shell instead: `dotnet run --project src/Act.App --launch-profile electron`.
- Stop your instance when you no longer need it, so the port is free for me.

### My board is not your test data (HARD RULE)

**The `Act.App` configuration shares my live store.** It runs the http profile, so
`ASPNETCORE_ENVIRONMENT=Development`, so `ActDataDirectory` resolves to
`%LOCALAPPDATA%\ACT.Development` — the *same* root my own app uses, on the *same* port. Your
instance and mine are then one database, one archive and one attachments folder.

**Never run a destructive action against it.** Emptying the archive, deleting cards, purging
— these act on *every* matching row, not the ones you made. A session once emptied
the archive several times without reading it and lost an attachment off a live task; whatever
else was archived went with it, and none of it is recoverable.

So: **anything that writes or deletes goes in a sandbox store of its own.** Verified route —
run it yourself with an explicit data directory and a port that is not 5210, then point the
browser pane at it with a url rather than a launch configuration:

```powershell
dotnet run --project src/Act.App/Act.App.csproj --no-build --launch-profile http -- `
    --ACT_DATA_DIR=$env:TEMP\act-agent-sandbox --Urls=http://localhost:5290
```

then `preview_start` with `{"url": "http://localhost:5290"}`. Both flags reach
`builder.Configuration` as command-line values (`ActDataDirectory.OverrideKey` is
`ACT_DATA_DIR`); confirm with the `Data directory …` line the log opens with. Use the
`Act.App` configuration only for **read-only** looking, and say so when you do.

- Startup **sweeps and purges delete files**, so a sandbox is also what stops a restart of
  yours reaping folders mine created — see `AttachmentSweep`, which now refuses to act on an
  empty board for exactly this reason.
- Kill only what you started. Before `Stop-Process` on port 5210, remember that instance is
  probably **mine**; ask, or use your own port and leave 5210 alone.

## Version control (HARD RULE)

- **Never `git add`/stage, `git commit`, or `git push` yourself unless I
  specifically ask for it in that message.** Make the file changes and stop.
  A prior request to commit/push does not carry over — each one requires its
  own explicit ask. "Fix it" / "address this" / reporting a problem is **not**
  permission to commit or push.

## Store compatibility (HARD RULE)

- **Never write a temporary mapper, a tolerant BSON converter, or any read-time
  shim to cope with data an older build wrote.** They read as harmless and then
  become permanent: nothing tells you when the last old document is gone, so the
  shim stays, and the store quietly holds two shapes forever.
- **Migrate the database instead.** Add an entry to `ActSchema.Migrations` that
  really rewrites the stored documents at startup and bumps the version. After it
  runs, only the new shape exists on disk, and the loading code knows exactly one
  shape.
- Until the first release, starting from a fresh `act.db` is still an acceptable
  answer for a breaking change — but say so out loud, because it costs my board.
  Once ACT has shipped, a migration is the only option.

## Guessing (HARD RULE)

- **Never ship a change you cannot justify, "just in case".** If you do not know
  whether something is needed — an extra permission, a retry, a guard, a wider
  timeout, a defensive branch — do not add it and label it unverified. An
  `# UNVERIFIED` comment is not a licence; it is the admission that the code
  should not be there yet.
- **Ask me to run the real thing instead.** Say exactly what you are unsure of,
  what I should run, and what result would settle it. I would rather run a
  release, a build or a click-through once than carry speculative code forever —
  and unlike you, I can see the failure.
- Once it is settled, the answer goes in the code as a fact, or in
  `docs/design-notes.md` if it is the kind of thing a future reader would try to
  undo. "We were not sure" never ships.
- This is the same failure as the read-time shim above: something added on a
  maybe, which nothing ever tells you it is safe to remove.

## Tests (HARD RULE)

- **Every change ships with its tests, in the same pass.** New behaviour gets new
  tests; changed behaviour gets its existing tests updated. Do not report a change
  as done with the tests "to follow" — that is the version that never arrives.
- **Aim for full coverage of what you touched**, and mean it: not just the happy
  path, but every refusal, every boundary, every branch a bad argument reaches.
  The paths that go untested are exactly the ones no screen shows you.
- **Put each test in the cheapest tier that can hold it** — see the *Testing*
  section of `docs/development.md` for the tiers. Pure logic goes in `Act.Core.Tests`; anything
  needing the app's service graph goes in `Act.App.UiTests` (`ComponentTest` has
  the graph already assembled); the browser tier is for what only a browser can
  show. A service is *not* exempt because it lives in `Act.App` — only the
  stateful pumps are, and they are exempt because they are pumps, not because
  they are app-level.
- **When you finish a feature, audit it before saying it is done.** List the
  types you added, grep the test projects for each one, and say plainly what has
  no test and why. Nothing found this way is embarrassing; the same gap found
  later is.
- **A failing test is a finding, not an obstacle.** Diagnose it before changing
  it, and never widen a timeout, loosen an assertion or delete a case to get to
  green. If the test is right and the code is wrong, fix the code; if a test is
  genuinely wrong, say so and say why.

## Communication

- Always reply to me in **English**.
- **Keep response summaries short.** A few lines, not a report. State what
  changed and anything I must decide; skip tables, restatements, and
  play-by-play of how you verified it.

## UI library

- **The UI library is Radzen.** Always prefer a Radzen component over raw HTML
  (`RadzenCard`, `RadzenStack`, `RadzenText`, `RadzenBadge`, `RadzenSelectBar`,
  `RadzenProgressBar`, …). Reach for raw markup + custom CSS only for the
  signature flight-strip details Radzen cannot express, and keep that CSS
  minimal.

## Coding conventions

- **The build must end with zero warnings.** Not "no new warnings" — zero, tests
  included. Before you report a change as done, run
  `dotnet build Act.slnx -c Debug` and check the count; fix whatever you
  introduced, and fix what is already there if you touched the file. Suppress a
  warning (`#pragma`, `NoWarn`) only when the analyzer is genuinely wrong, and say
  so in the reply.
- **Always use a code-behind `.razor.cs` file** for component logic — never an
  `@code` block inside the `.razor` file. Markup stays in `.razor`, C# stays in
  the partial class.
- **Always inject dependencies through the constructor** (primary constructor on
  the partial class), not with the `[Inject]` attribute.
- **No braces around a single-statement `if`** — put the statement on the next
  line, indented:

  ```csharp
  if (language == current.Language)
      return;
  ```

  Braces stay when the body has two or more statements.
- **Do not write code comments.** Only add a comment when either:
  1. I explicitly ask for it, or
  2. the code is not final — a placeholder, stub, or otherwise pending final
     implementation. In that case the comment must flag the pending state.

  Finished code ships without comments; let clear names and small functions
  carry the meaning.
