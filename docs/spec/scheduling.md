# Scheduling & queue policy

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

Replaces any attempt to *estimate* task effort (unreliable — agentic cost is
unpredictable and remaining budget isn't cleanly readable). Instead the user
sets **per-task intent** for *when* a task may start.

## Per-task `schedule`

Chosen in the new-task modal (default **Manual**) and editable while in Ready, so
moving a card to Ready needs no extra prompt. Shown as a badge only once the card
is in Ready — it is launch intent, and nothing launches from Preparing:

- **Manual** — never auto-launched; user starts it by hand.
- **Now** — as soon as possible (subject to cap + backpressure below).
- **Next 5-hour window** — at the next session-window reset.
- **Specific date & time** — user-picked datetime.

## Runner logic

A task becomes **eligible** when its `schedule` condition is met, then launches
only if nothing is holding it. **One pure evaluation answers both questions** —
`Act.Core/Scheduling/LaunchQueue.Evaluate` returns the ordered launch list *and*
the per-card hold — so the chip a Ready strip shows and the decision the runner
acts on cannot disagree. `Act.App/Sessions/QueueRunner` is the only stateful
part: a coalesced pass on a board change, a usage reading, a settings change, or
a 20-second backstop tick.

- **A relative schedule is resolved to an instant once, when the card is armed**
  (`card.eligibleAt`). "Next window" means the boundary that was next *then*; a
  value recomputed from live usage would slide forward every time that boundary
  passed, and the card would be perpetually one window from starting. It also
  makes the queue survive a restart. It is cleared when the schedule changes and
  when the card launches.
- `maxConcurrent` (default **5**) — cap on tasks past the launch boundary that are
  not yet awaiting sign-off: Executing, plus Your turn on any badge but
  `to review`. A parked prompt owns an agent process and its pty and costs what a
  working one costs. **`error` and `killed` count too, with no process left**, and
  that is deliberate: each is a failure the user has not dealt with, one Retry from
  being live again, and a queue that kept launching past a growing pile of them
  would turn one broken task into twenty. The cap is a ceiling on work in flight,
  not a process count — which is why *Keep-awake*, which really is about processes,
  draws its line one badge tighter.
- **Usage backpressure (safety net)** — if an eligible task's usage window is
  spent it **waits for the reset** rather than erroring. A window at or above the
  ceiling names itself and the **latest** such reset wins (launching at the
  5-hour boundary against a spent weekly quota would only fail again); the
  account-level `limit_reached` flag names no window, so the **soonest** reset is
  taken, since being wrong there costs one re-check rather than days. A reading
  held past its own reset is not a limit, a window that has not started is not a
  limit, and **no reading is not a limit either** — an unreadable credential file
  must not freeze an overnight run, so the queue launches and lets the CLI be the
  one to refuse. So `schedule` is *intent*; cap and backpressure are *reality*.
- **Spent is 95%, not 100% — and it is a setting.** *Hold the queue at* (default
  **95**, any percentage 1–100, `UsageCeiling`) is the percentage **at or above
  which** a window counts as spent: at the default a card launches at 0%, at 49%
  and at 94%, and waits from 95% up. It is one threshold, not a band — nothing is
  held below it. 100 is the wrong place to stop for three compounding reasons: the
  percentage ACT holds is up to `Usage:PollSeconds` old (180 s by default), a
  5-hour window moves about a third of a percent a minute, and the vendor rounds
  to a whole percent before ACT ever sees it — so a reading of 99 can be a window
  that is already full. And a task launched into the last of a quota does not fail
  cleanly: it starts, burns the remainder and stops **mid-run**, which costs more
  than waiting for the reset would have. How much quota to leave for interactive
  work is the user's call, so a low ceiling is honoured rather than second-guessed;
  the bounds only keep the number a percentage, and the floor is 1 rather than 0
  because a ceiling of zero would hold every card forever — which is what the
  master switch already says plainly. Manual launches are unaffected — this is a
  rule about what ACT starts on its own.
- **The queue is the Ready column, read top to bottom** — see *Ordering a
  column*. Nothing weighs one task against another: a queue that reorders itself
  is a queue nobody can predict. A due instant decides *whether* a card is in the
  queue, never where in it: a card that is not due yet is not queued at all, and
  one that has come due sits where the lane shows it.
- **A prerequisite the store no longer has counts as satisfied.** The alternative
  is a card that can never launch and says nothing about why.

## One task per working directory

A folder is one working tree, and two agents editing it at once is the failure
mode that costs *work* rather than time: the second reads a file the first is
halfway through rewriting, and both commit over each other. So a Ready card does
not launch while another card is working in the same directory.

- **A folder is held until the holder is Completed, not until its turn ends.** A
  card in Your turn still owns a live session parked at its prompt and whatever
  it left in the tree, so it is no freer than it was mid-turn. Executing and Your
  turn hold; Preparing, Ready and Completed hold nothing. This is why the rule
  reads off the *column* rather than off `registry.IsLive` — a killed card whose
  process is gone still has the tree it left behind.
- **Compared resolved, never as typed.** A card stores `workingDir` the way the
  user wrote it, so `~/dev/act`, `C:\dev\act\` and `/dev/act` must not read as
  three folders. The guard resolves both sides through `IWorkingDirectories`
  first (case-insensitively on Windows), which is the difference between a guard
  and a guard that silently never fires.
- **`preventConcurrentWorkingDir`** (*Settings → Execution*, **on** by default)
  is the policy; **`allowConcurrentWorkingDir`** on the card is the escape hatch,
  for the case that is genuinely fine — a `plan`-mode task that mutates nothing,
  running beside the implementation task in the same repo. The task form shows
  the per-card switch only while the global one is on: an exemption from a rule
  nobody is enforcing reads as if it did something.
- **The exemption is read off the card being started**, never off the one already
  there. "Run this one anyway" is a decision about the task being launched, and
  the card holding the folder has no say in what runs beside it.
- **Only a Ready launch is guarded.** A retry and a post-restart re-attach belong
  to a session that already holds that folder, so guarding them would strand the
  very card the folder is busy for.
- **Refused for a button press, queued for the runner** (both).
  A launch the user just pressed is refused: the card stays in Ready untouched
  and the message names the card in the way, so nothing needs undoing when the
  folder frees. The runner reads the same non-null answer as *not yet* — the card
  waits under a `folder #NNNN` chip and starts on its own once the holder is
  signed off. The rule is unchanged; only what each caller does with the answer
  differs. One addition: the runner also asks it of the cards **it is about to
  launch in the same pass** (`WorkingDirConflict.SameFolder`), which
  `Blocking` cannot answer because none of them is Executing yet.
- **It reports amber, not red.** `LaunchResult.Wait` is its own outcome beside
  `Refused` for exactly this: nothing is broken — the card, the prompt and the
  install are all fine, and the folder clears on its own. A red *Launch failed*
  would send the user hunting for a fault that does not exist, so this is a
  warning headed *Not started yet*. Every genuine failure — a bad path, a missing
  binary, a rejected launch config — stays red.

## Spawned-task schedule defaults

- **Agent-emitted** (plan follow-ups): default **Manual** — nobody chose them at spawn
  time, so review-before-run is safer. This is the only kind of spawn there is; see
  *Task spawning & lineage*.
- **Settled:** the agent may override it per call with `now` or
  `next_window`, and **`same` is not offered** — a parent's schedule is a trigger that
  already fired, so inheriting it means either a past date or an immediate launch. The
  reasoning is in *Agent ↔ ACT contract*.

## Master switch

A global **"pause all auto-execution"** toggle (auto-execution on by default).
When paused, no task auto-launches regardless of its `schedule`; per-task
**Manual** remains the per-task opt-out. Manual launches still work while paused.
It lives in *Settings → Execution* **and on the board**, in the control row beside
the filter box, where the old top-bar `auto-exec on` chip became the switch itself:
it is what you reach for when something is going wrong, and two navigations away is
the wrong place for that. The chip loses its green glyph and turns amber when paused.

- **Why it is on the board, not the top bar.** The switch governs the board and
  nothing else: paused, the only thing that changes is which Ready cards start, which
  is a sentence about the board. The top bar is the *app's* furniture (identity, usage
  meters, archive, settings), and the board is one back-arrow from every page, so
  it stays reachable from anywhere. It sits with the density toggle in the
  control row — the board's own controls, together.

## Ready-card indicators

A Ready strip carries two chips, and the split is the section's own line —
**intent** below, **reality** in the badge slot:

- **Intent** is the `schedule`, on the dashed chip it always had: `manual`,
  `now`, `next win`, `⏱ Mar 3 09:00`.
- **Reality** is the *hold* — why this card is not running right now — one chip
  per reason: `queued 5/5` (cap full) · `folder #1041` · `after #1043`
  (dependency) · `usage 2h14m` (limit reached, counting down to the reset) ·
  `paused` · `agent off`. Precedence is most-durable first (paused, agent off,
  usage, folder, dependency, slot), because only one fits. **No hold chip at all**
  when the card is `manual`, or armed and simply not due yet: the intent chip
  already says when, and a chip claiming otherwise would imply ACT intends to
  start it.

**A hold is not a `Badge`.** `Badge` is persisted and drives the blink and the toast; a hold is derived every render, stored nowhere,
and must raise no attention — nothing is wrong and every one of them clears on
its own. It borrows the badge *slot*, which a Ready card leaves empty, and the
muted colour the `quiet` chip established.

## Keep-awake (prevent sleep)

A configuration option to **prevent the computer from sleeping** while there is
auto-work to protect — otherwise the overnight queue stalls when the machine
sleeps. OS-specific under the hood (Windows `SetThreadExecutionState` / Linux
`systemd-inhibit` / macOS `caffeinate`); exposed as one ACT setting.

It is respectful rather than unconditional: the hold is taken only while a **live
agent process** exists — Executing, or Your turn on `needs permission` /
`needs answer` / `compacting` — or a **Ready card could actually launch on its
own**, meaning a non-`manual` schedule with auto-execution not paused. An idle
board is left to sleep, a `manual` Ready card is waiting for a human who is
evidently not there, and pausing releases the hold unless something is already
running, because nothing can start while the switch is off.

**This is one badge tighter than the concurrency cap, and the two must not be
merged.** The cap counts `error` and `killed` because they are work in flight the
user still owes an answer to; sleep is about processes, and those two have none —
keeping a laptop up all night for a session that died at 3am protects nothing. The
inhibitor is therefore owned by the queue runner, which is the only thing that
knows what is pending — not by the settings service, which only knows that the
user wants a hold when there is something to hold for.

## Build-time dependencies (not blockers)

- ✅ **"Next window" needs the 5-hour reset time — solved.** Neither computing it
  from tracked activity nor reading `/usage` is necessary: both vendors serve the
  reset instant over HTTP (see *Usage indicator* below), so `schedule` reads the real
  boundary rather than estimating one.
- ✅ **Weekly reset** — likewise served, so it needs no per-account configuration.
- **Unattended overnight runs require a non-prompting `permissionMode`** (`auto`,
  `dontAsk` or `bypass`). This is now a hard requirement rather than a strong
  suggestion: since ACT never answers a prompt, a prompting mode leaves the task
  parked at a TUI prompt that nobody is awake to answer, all night, holding a
  concurrency slot. `acceptEdits` only covers edits, so it still stalls on the
  first `Bash` call it wants approval for. **The task form warns** when `schedule`
  is not `manual` and the mode is `default` or `acceptEdits` — a warning and never
  a block, since the combination is legal and the user may have a reason; what it
  must not be is a surprise at 3am.
- **`maxConcurrent` now caps live terminals too.** Every card past the launch
  boundary holds a live agent process, so the cap is a real resource ceiling, not
  just a politeness setting.
- Verify at build: clean mid-task resume after a rate-limit interruption.

## Usage indicator

The top bar carries a live meter per usage window per agent — percentage used and how
long until it resets. It is the same data `schedule` and backpressure reason about,
made visible.

- **Two lines each, and the strip's height is why.** Three stacked rows (label +
  percent, bar, reset) would make the meter the tallest thing in the top bar and
  grow the whole strip by a third. So everything you read is on one line —
  `CLAUDE 5H 3% · in 4h 50m` — with the bar under it spanning the meter's width.
  At 19px that fits inside the 28px the buttons already claim, so the strip is
  sized by its buttons.
- **The countdown is on the strip; the clock time is in the tooltip.** Three windows ×
  "in 4h 50m · 3:19 p.m." does not fit a one-line strip at a normal window width, and
  the countdown is the half you act on. The tooltip still carries the agent, the window,
  the absolute reset stamp and when the reading was taken.
- **The reset never hides.** At narrow widths the meters shrink and the countdown
  ellipsizes as a last resort — nothing about the usage is width-gated, because a
  meter that disappears reads as a bug.

**Source: a live HTTP endpoint per agent**, authenticated with a bearer token read
from that CLI's own credential file, polled **once every 3 minutes and never faster
than once a minute**. Endpoints, response
shapes and the alternatives that were rejected (Claude Code's `statusLine` payload,
scraping `/usage` from a pty, deriving from transcripts, Codex's rollout
`rate_limits`) are recorded in **`docs/findings/agent-usage.md`**. Read it before
touching usage code.

Five rules the design turns on:

- **A window is named by the length the server declares.** A paid Codex plan reports
  5-hour plus weekly; a free one reports a single 30-day window. Two hardcoded
  captions would mislabel a real account.
- **A reading held past its own reset reports zero, not the number it was given.**
  ACT polls, so nothing ran in between; the old percentage describes a window that no
  longer exists. Rendered subdued, because it is inferred from this machine alone —
  usage from another machine, the web, or an IDE extension counts against the same
  quota and is invisible here.
- **ACT reads those credential files and never writes them.** Each CLI owns and
  refreshes its own; ACT re-reads before each poll and never attempts a refresh,
  because refresh tokens rotate and racing the CLI could invalidate the user's login.
  The token is never logged, never persisted, and goes nowhere but the vendor.
- **The poll rate is a budget, not a preference.** Neither endpoint is documented or
  promised, so `Usage:PollSeconds` defaults to 180 and is *clamped* to [60, 3600] — the
  floor is not configurable away. The gap is measured from the end of one request to the
  start of the next, so a slow endpoint is never asked again the instant it answers, and
  a failure that reached the network doubles the wait up to a 15-minute ceiling
  (`UsageBackoff`). A failure decided locally — no credentials, an expired Claude token —
  spends no request and so keeps the base interval, because backing off would only delay
  the recovery. A disabled agent is not polled at all.
  - **A rewritten credential file ends the wait early.** The backoff protects the
    vendor's endpoint, not the local file: while an outcome a new credential can cure is
    showing (not signed in, expired, sign-in required, refused), the pump watches the
    credential file, and a change probes again immediately — held to the one-minute
    floor — and resets the backoff (`UsageWake`). Any CLI run refreshes the token and
    rewrites that file, so a login renewed outside ACT reaches the bar in about a minute
    instead of after a backoff that may be 15 minutes deep.
- **Unavailable is a state the bar shows, not a silence.** Every failure path resolves to
  a `UsageAvailability`, and the agent's meters are replaced by an **unavailable chip** —
  the agent name, the word *unavailable* in the error colour, and the cause in one mono
  line (*not signed in*, *token expired*, *token refused*, *no connection*, *unreadable
  reply*), with the full sentence and the last-checked time in the tooltip. Dropping the
  meters silently, which is what the first version did, left no way to tell "you have used
  nothing" from "ACT has been locked out since this morning". The chip keeps the meter's
  box and rules so the bar does not jump when a quota comes back.

Nothing about the indicator can break the board: every path ends at a reading or at a
named unavailability, never at an exception the UI has to handle.
