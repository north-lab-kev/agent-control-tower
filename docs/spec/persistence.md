# Persistence & archival

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

Two threads with different answers: data retention, and session lifecycle.

## Data retention — retain, don't destroy

Local single-user app, so storage isn't a real constraint; the bias is to keep
history.

- **Active board** shows all non-Completed cards plus Completed ones within a
  recent window.
- **Auto-archive** Completed cards after a **configurable window (default 10
  days)**: they leave the board but stay in LiteDB — fully searchable, with
  `transitions[]`, `metrics`, lineage, and `initialPrompt` intact (audit +
  analytics value, cheap to keep). **Not the same axis as `deletedAt`** — that marks
  what the *user* removed and expects to find in the archive; auto-archive is a
  retention policy on cards nobody deleted, so it has its own marker, `archivedAt`.
  Sharing a field would make "restore" mean two different things.
  - **The window is measured from the sign-off (`completedAt`), never from the last
    thing that happened to the card.** The setting reads "archive after N days", and a
    Completed card whose session is reopened and read must not silently earn itself
    another N days. A Completed card with no `completedAt` has no age to measure and is
    left alone rather than dated by guess.
  - **Restoring by hand wins for good.** A card the user pulls back out of the archive
    carries `keepOnBoard`, and retention never takes it again — a policy that keeps
    undoing an explicit decision is a bug, not a policy. `CompletedRetention`
    (`Act.Core/Rules`) is the whole rule; `BoardState.ApplyRetentionAsync` stamps the
    cards it names, and `RetentionPump` runs it hourly, at startup, and on every
    settings change so turning the policy on takes effect while the user is watching.
  - **The archive page shows both axes in one list**, because they are the same thing to
    look at — a card that is no longer live and can be brought back. The row says which
    of the two put it there, and *Clear archive* empties exactly what the list shows.
- **Delete is always soft.** The task page's footer carries an icon-only delete, left-
  aligned and deliberately far from Save, available in **any** column. It sets
  `deletedAt` and the card leaves the board — nothing is destroyed, so it asks nothing:
  **the archive is the undo**. The only irreversible act in the app is emptying that
  archive, and it lives there rather than on the card.
  - **Archive page** (`/archive`, from the top bar): everything off the board — deleted or
    auto-archived — newest first, each restorable with one click, plus **Clear archive** —
    the single permanent delete, behind an inline confirm. Restore puts a card back in the
    column it left.
  - **An archived card opens, and everything inside it is read-only.** The row's title is a
    link to the card's own routes, because reading a task is most of why the archive keeps
    it — the prompt it ran with, its folder, its settings, its timeline. What it is not is a
    task: `TaskEditing.CanEdit` is `isOnBoard`, so **every** field is frozen — not just the
    launch inputs a launched card freezes — and Save and Delete are gone rather than
    disabled. **Its terminal does not start**: no launch, no resume, no restart, no desktop
    handoff, and the terminal face renders a note instead of an xterm rather than a black
    pane that never paints. `SessionRestore`, `CardCompletion`, `CardRetry` and
    `SessionLauncher.CanLaunch` all read `isOnBoard` for the same reason, so an
    auto-archived card is as inert as a deleted one and startup never resumes either.
    Duplicate and *Save as a template* stay, because neither touches the archived card.
    Restoring it is what makes it a live task again, and that lives on the archive page.
  - **Leaving it returns to the archive, not to the board.** The back arrow, Cancel and Escape
    all land on the list the card is actually on — `CardExit`, shared by the three faces so
    they cannot disagree — and the arrow is labelled with the page it opens. The two exits
    that keep the board are the save and the archive itself: both are moves *of* the card,
    made from the board, and the board is what shows the result.
  - **Kill before archiving.** A card with a live session tears the process down first.
    Archiving while its agent kept working would leave a process editing a directory with
    nothing on the board pointing at it — precisely the state ACT exists to prevent. The
    session does not come back on restore; the card does, and can be launched again.
  - **Follow-ups are a question, not a guard.** A card with live children opens a **dialog**
    asking whether to archive them too or keep them, and it **names the follow-ups** rather
    than counting them — nobody can weigh "2 tasks". It is the one thing the user must
    decide, and separate from "are you sure", which soft delete makes unnecessary.
    **Lineage is left intact either
    way**, so an archived parent still knows its children and a restore finds them
    attached. Purging is where the both-sides rule finally applies: a purged card is
    unlinked from any surviving parent's `children[]` before the row goes.
  - **Restore is per card, never a subtree.** Children were archived by their own
    decision and come back the same way, so restoring a parent never silently resurrects
    work the user meant to be rid of.
- **Duplicate copies the intent, never the run.** Next to the task page's delete, and on
  every archive row beside Restore, a **duplicate** makes a new card carrying only what to
  run, where and how — title, prompt, working directory, agent, launch config and
  schedule. The copy is titled **"Copy of &lt;title&gt;"** (localised), so two cards that
  differ only by number never look identical on the board. It lands in **Preparing** with a
  fresh id and number, and with no session
  id, badge, metrics, observed model, last message or transition history: a copy is a task
  that has not started, not a fork of a session. **Lineage is dropped** — the follow-ups a
  card spawned belong to the run that spawned them, so a copy has neither children nor a
  parent. Duplicating an archived card leaves it archived; the copy is live, which is the
  point — it is how a finished or abandoned task gets run again without disturbing its record.
- **Artifact housekeeping:** ACT writes nothing into the user's working directory, so
  there is nothing there to clean up — the generated launch config (settings / profile /
  MCP config) lives in ACT's own data directory and is removed when the session ends. It
  **never** touches the agent's transcripts (not ACT's to delete, and needed for resume).

## Finding a card — two filter boxes, not a search page

Retention keeps everything, so the question retention creates is *where did that task go*.
The answer is a filter box on each of the two surfaces a card can be on — the board and the
archive — narrowing what is drawn, in place, in the strip or the row the user already reads.

- **A query matches the number, the title, the working directory and the initial prompt.**
  The first three are how a card is identified; the fourth is where the recall value is.
  Agent, model and transition notes are deliberately not searched — they match far too
  broadly for a box whose job is to narrow. Terms are whitespace-separated and **all** must
  match; matching folds case and accents (French is a shipped UI language), and a term of
  digits, with or without a leading `#`, also matches the **number by prefix** — `#104`,
  `104` and `1042` all find `#1042`. No ranking: each surface keeps its own order.
- **The filter narrows what is *drawn*, never what is counted.** A board column header reads
  `3/12` while a query is live, so a filtered board can never be mistaken for the real one.
- **And it can never hide the fact that a card needs you.** Attention is rendered per strip,
  so a needs-you card that falls out of a query would simply vanish — the one thing the board
  exists to prevent. Its column therefore carries a muted amber `N hidden` chip counting the
  needs-you cards the query is holding back.
- **The board says what it cannot show.** The board has no archived cards on it, so while a
  query is live it carries a link reading *"N in the archive ›"* which opens the archive with
  the same query applied (`/archive?q=…`). That is the price of filtering in place rather than
  building a `/search` page — a page was rejected as a third list of cards, with its own row
  design and its own ordering rule, showing what the two existing surfaces already show.
- **Clear archive disappears while a filter is up.** *Clear archive* empties exactly what the
  list shows, and a filtered list is not what it would empty; the one irreversible action in
  the app must not be able to mean something other than what is on screen.
- **The query is view state**: never persisted, never stored, cleared by `Esc`, by the clear
  button and by leaving the page. Nothing is indexed — every card is already in memory.

## Session lifecycle — resumability rides the transcript

Resumability does **not** depend on ACT keeping anything alive. Retry
(Your turn → Executing) and reopen (Completed → Your turn) work via `--resume
<sessionId>` against the agent's **on-disk transcript**, which persists
independently of ACT.

- Per the liveness model, ACT keeps the terminal alive for the whole active life
  of a card and tears it down on kill or completion. Opening a **Completed** card's
  terminal, or any card whose terminal died with ACT, re-spawns via `--resume` into a
  fresh terminal. That re-spawn is **automatic** on open for every bound card that has
  been launched, Completed included, and additionally at startup for the machine
  region (see *Edge cases*). What Completed still waits for is the **reopen** — the
  move back to Your turn — not the terminal.
- **Caveat:** the transcript is outside ACT's control, subject to the agent's
  retention or user deletion — so a very old Completed task may no longer be
  resumable.
- **Fallback:** reopen/retry attempts `--resume`; **on failure, offer to
  start a fresh session seeded with the stored `initialPrompt`** rather than
  failing silently. Record which path was taken.

## Where the data lives

The store is a single LiteDB file at **`%LOCALAPPDATA%\ACT\act.db`** (on Linux,
`~/.local/share/ACT/act.db`) — **outside the installation folder**, so data
survives an upgrade or a reinstall. `ACT_DATA_DIR` overrides the directory (env
var, appsettings key, or CLI arg); it's resolved once at startup and passed into
infrastructure, which never reads the environment itself.

**The folder is per hosting environment.** `Production` — the installed app — owns
plain `ACT`; every other `ASPNETCORE_ENVIRONMENT` gets its own sibling,
`ACT.<Environment>`, so a `Development` run lands in `%LOCALAPPDATA%\ACT.Development`.
Everything ACT writes hangs off that one root — `act.db`, `logs/`, `attachments/`, the
generated agent config files — so the split isolates the whole working set, not just the
board. `ACT_DATA_DIR` still wins over both: an explicit path is the answer, and the
environment suffix is only the default for the machine nobody has configured.

The **listening port is split the same way**, because the two apps have to be able to run
at once: `appsettings.json` pins the installed app to `http://localhost:5200` and
`appsettings.Development.json` pins the dev loop to `http://localhost:5210`. The hook port
needs no rule — it is allocated free and remembered *in the store*, so separate stores
already mean separate hook ports.

## Single instance — one process owns the data file

**ACT is a singleton: launching it again focuses the window you already have.**
LiteDB opens the file with a single-writer lock, and two boards driving the same
agent processes and hooks would fight anyway — so a second instance is never the
right answer.

- **Enforced in the Electron main process** (`singleInstance` in the generated
  manifest, set from `<ElectronSingleInstance>` in `Act.App.csproj`): the second
  launch requests the instance lock, loses, and **quits**. `app.quit()` is async,
  so it does begin spawning its backend first — Electron tears that child down on
  exit, so it never becomes a second writer. Verified: a second launch of the
  packaged app leaves the first instance's process tree and ports untouched.
- **The first instance reveals itself** on the `second-instance` event: restore
  (if minimized) → show → focus. Wired in `Program.cs` via
  `RequestSingleInstanceLockAsync`, which also covers the window being hidden
  rather than merely minimized.
- **Consequence for development:** a browser-mode `dotnet run` and the packaged
  desktop app are separate processes with separate instance locks, so the lock
  never stops the second one — only the LiteDB single-writer lock on a shared
  store would. The per-environment folder above removes that: a
  `Development` run opens `ACT.Development\act.db` on port 5210 while the installed
  app keeps `ACT\act.db` on 5200, and neither sees the other. Pointing one of them
  somewhere else with `ACT_DATA_DIR` is still available and is the only answer when
  two runs share an environment.
