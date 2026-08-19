# State model

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

A card carries two independent things: a **column** (where it sits in the
workflow) and a **badge** (the live health/reason of its session). Columns can
be human- or machine-controlled; **badges are always automatic.**

## Summary

| # | State | Control | Enters by | Exits to |
|---|---|---|---|---|
| 1 | Preparing | Human | user creates task (initial) | Ready |
| 2 | Ready | Human | manual from Preparing, **or spawned** from a completing task | Executing (launch) · Preparing (back) |
| 3 | Executing | Machine | auto, on launch | Your turn |
| 4 | Your turn | Machine | auto: permission / question / error / `Stop` | Completed (drag) · Executing (observed input, or Retry) |
| 5 | Completed | Human | **dragged** from Your turn | Your turn (reopen) |

**Control arc:** human → machine → human. Control starts with the user
(Preparing, Ready), passes to the agent at launch (Executing, Your turn), and
returns to the user at the end (Completed). ("Control" here means who drives
movement *out of* a state. A spawned task is *created* into Ready by
machine/agent, but the user still controls its exit — gate the launch or send it
back to Preparing — so Ready stays human-controlled.)

**Launch boundary (key rule):** the moment a card enters Executing, the user
can no longer move it by hand. Columns then change *only* through automated
transitions — driven by **hooks** (in-session events) and by **process signals**
(exit code / no-activity timeout, which hooks can't report, e.g. a crash). The
exceptions are the two moves at the far end of the workflow — the **Your turn →
Completed** sign-off and the **Completed → Your turn** reopen. Neither overrides
a live session: the first *ends* one, the second re-enters the workflow. So the
rule still holds: no manual moves while a session is actively mid-flight, which
is why **Executing** is the one column that neither lifts nor accepts a drop.

**Badges (orthogonal to columns, always auto):** `running`, `needs permission`,
`needs answer`, `error`, `killed` (nothing stamps it today — see *Restart
terminal*), `compacting` (context being compacted), `to review` (finished, awaiting
sign-off). One event can both move a card *and* stamp its badge. `compacting`
occurs *during* Executing. There is deliberately **no `stale` badge** — a card
that has gone quiet says so beside its badge instead of in it (see *No stale
badge — the quiet chip instead*).

**Why one column and not two.** A *Needs feedback* column beside a *To review*
column would say the same operational thing — *the ball is in your court* — and
differ only in **why**, which is exactly what the badge already carries. They
would also behave identically: activity observed in the terminal returns a card
to Executing from either, under the same reason code. One column makes the
badge the sole carrier of the reason, and gives the column a single meaning the
attention blink and the notification rules can both key off. The consequences,
all deliberate:

- **Ordering replaces adjacency.** Two columns implied a priority by sitting
  side by side; one column has to state it. Your turn is ordered like every other
  column — newest arrival first, and whatever order the user drags it into after
  that (see *Ordering a column*). It is deliberately not ranked by badge: a rank
  the user cannot override decides for them which of twenty waiting cards to look
  at next, and only they know that. The badge still says *why* each one is
  waiting, and the blink still says *that* one is.
- **Sign-off is gated by the column, not the badge.** Any card in Your turn can
  be completed, including one that errored or was killed — previously those had
  no path to Completed except a round trip through the terminal.
- **The blink covers the whole column,** in three colours: amber for a blocked
  prompt, red for `error`/`killed`, and the calm review blue for `to review`. So
  a scan still separates "something is stuck" from "something is done" without
  reading a word.

## The states

**1. Preparing** *(initial, human-controlled)*
Every task is born here. The user is drafting the prompt / defining the task;
no agent session exists yet. Exit is always manual, to Ready.

**2. Ready** *(queue, human-controlled)*
The prompt is finalized and the task is queued but not yet launched — no session
exists yet. Reached either manually from Preparing, or by being **spawned** from
another task (see Task spawning & lineage). Exits: launch → Executing, or
manually back to Preparing if the prompt needs work.

The manual move has two gestures, and both are the same transition (recorded as
moved by hand): the **drag** on the board, and **Save + Ready** on the task
form, which saves the card and moves it in one press so drafting a task and
queueing it is not two trips to two surfaces. The form's button is offered only
while the card is still in Preparing — it performs the move the board allows, and
never a second door into any other column.

**3. Executing** *(machine-controlled)*
First automated transition. On launch, ACT generates the session UUID, spawns
the agent CLI (e.g. `claude`) in the task's working directory with that
`--session-id` and the prepared prompt, and binds `session_id → card`. The task
is actively being worked (`running`). Auto-exit: to Your turn, whether the
session blocked or finished — the badge says which.

> **Liveness model** (see Liveness & the embedded terminal). ACT spawns the
> agent's **interactive TUI under a pseudo-terminal it owns** and keeps that
> process **alive for as long as the card is active** — through Executing and
> Your turn alike — tearing it down only on kill or completion. So "working" =
> the TUI is mid-turn; `to review` = the same live TUI parked at its prompt;
> "send input" = **the user typing into that terminal**, which ACT hosts
> full-screen for the card.

**4. Your turn** *(machine-controlled → hands back to human)*
Everything the user is on the hook for, in one column; the badge says which kind:

- **needs permission** — a permission prompt is open in the TUI.
- **needs answer** — the agent asked a question.
- **error** — crash or non-zero exit.
- **killed** — **nothing stamps it** (see *Restart terminal*). The badge is kept
  so a card stored with one still reads back and can still be signed off.
- **to review** — the turn is over and nothing is blocking: the agent finished cleanly
  (`Stop`, no question, no error), or **the user stopped it from the keyboard** (Ctrl+C
  or `Esc`), which produces no hook at all and so is reported from the keystroke (see
  *Rules engine* → *The interrupt is the one turn end no source reports*). Either way
  there is work on the screen to read.

The card is a *report* that the TUI is waiting — **ACT never answers on the
user's behalf** (see Hooks are observability, not control). In every case the
session is still alive and parked at its prompt (or, for error/killed, gone but
resumable), so the exits are the same regardless of badge:

- **→ Executing.** The user acts in the terminal — answers a prompt, or sends
  reviewed work back — and ACT sees the session move again (badge returns to
  `running`). For **error**, **Retry** does the same thing explicitly (see Error
  handling & retry).
- **→ Completed.** The user's explicit sign-off, available on any card in the
  column: a crashed or killed task is as legitimately done-with as a reviewed
  one. On the board it is a **drag onto Completed** — the same gesture as every
  other column change, so the strip carries no sign-off button; the session view
  keeps its **Complete** action for a card you are already inside.

Cards are ordered like every other column's — see *Ordering a column*.

**5. Completed** *(terminal-ish, human-controlled)*
The user marks the task done from Your turn. Not strictly terminal: it has one
manual exit, **reopen → Your turn**, for when the user forgot some feedback.
From there the normal Your turn exits apply.

- **The gesture mirrors the sign-off** — a **drag back onto Your turn** on the board,
  plus a **Reopen** action in the session view for a card you are already inside, which
  is where the user usually is when they find the thing they still wanted to say.
- **It un-stamps `completedAt`**, so the retention sweep (which dates a card from its
  sign-off) cannot archive a card that is no longer signed off. That is why it is its
  own rule (`CardReopen`) and not a plain manual move, exactly as the sign-off is.
- **The badge it lands on is `to review`.** Nothing was observed; the card is simply
  back in the user's court with work to look at.
- **It starts no session.** The terminal comes back the way it does for every bound
  card — by being opened (see *Session lifecycle*) — so the reopen is the column move
  and nothing else.

## Ordering a column

Every column is a **list the user owns**, and the same rule covers all five:
`Act.Core/Rules/CardOrder`, on a stored `order` per card.

- **A card lands at the top of the column it arrives in** — created, dragged,
  launched, moved by the rules engine, signed off, reopened. What just happened is
  what you want to read, and landing lower would bury a fresh sign-off — or a card
  that just started waiting — under twenty older ones. Nothing arrives in the
  middle of a lane the user has arranged.
- **So an untouched board is newest-first.** `number` descending is the tiebreak, so
  cards that share an order — everything stored before ordering existed sits at zero —
  read newest first too, rather than inverting the rule for the one case nobody
  arranged. The stamp walks *down* rather than renumbering the lane, so an arrival
  writes one row instead of all of them and `order` values go negative; they are a
  sequence, never a position.
- **A strip dropped on one of its own column-mates takes that card's place**:
  dragged down it lands after the card under the cursor, dragged up it lands
  before, and an insertion line on that edge says which while the drag is live.
  Crossing columns is the move it always was — the card lands first, not wherever
  the cursor was.
- **Ready's order is the queue's order.** The runner takes that column exactly as
  it is drawn (see *Runner logic*), so an unarranged Ready launches the most
  recently readied card first, and dragging a strip to the top is how you say "this
  one next" against that. Manual ordering is the reason this exists; ordering the
  other four is the same gesture doing the obvious thing.
- **The lane is ordered, not the filter.** A drop is resolved against the whole
  column, so reordering while a search is narrowing the board cannot silently
  reshuffle the cards it is hiding.
