# ACT — The interrupt no hook reports

What happens on the board when the user stops a turn from the keyboard, why the
keystroke had to become the signal, and what was measured before it did. Read this
before touching `TurnInterrupted`, `TurnInterruptKeys`, `TurnInterruptProfile` or
`TurnInterruptWatch` — and re-run the probe at the bottom on a CLI upgrade.

Measured against **claude-code 2.1.235** (under `PtyHost`, with the same `--settings` hook file a
real launch writes) and against **`codex-cli`** on `gpt-5.6-terra`. Both CLIs land on the same
profile in the end, but it stays per-adapter: nothing guarantees the next pair does.

**This file has been wrong twice, and says so on purpose.** The hook silence came first and is
solid. The keystroke's *meaning* has needed two corrections since — an open picker eats the key
(found when a working card was put up for review), and an `Esc` measured as inert on Codex that
was really a chord eating the keypress (found when an interrupted card stayed `running`). The
pattern is worth expecting: what a key *reports* is easy to measure, the *state* the CLI was in
when you measured it is the part that bites.

## The symptom

A card in Executing badged `running`, whose agent the user interrupted, **stays that
way for the rest of the session**. The CLI is parked back at its prompt with work on
the screen; the board says it is still working. Nothing corrects it: the next thing to
move the card is the *user's next message*, so a card interrupted and left alone claims
`running` indefinitely, and the only hint is the `quiet` chip after fifteen minutes.

## What the CLI actually reports — nothing at all

An HTTP hook receiver was pointed at a real session and every hook ACT subscribes to
was logged with its arrival time. Three runs, one prompt long enough to interrupt
mid-answer ("write a 1200-word essay…", no tools):

| Run | What was sent | Hooks after it, within the window |
|---|---|---|
| Ctrl+C mid-turn | `0x03` at 13 s | **none, for 180 s** |
| `Esc` mid-turn | `0x1b` at 13 s | **none, for 45 s** |
| Nothing (control, short prompt) | — | `Stop` at 2.8 s |

The control run is what makes the other two conclusive: the same harness, the same
settings file, and a turn that ended on its own **does** fire `Stop`. An interrupted one
fires nothing — not `Stop`, not a `Notification` of any type, not `SessionEnd`. Both
interrupt keys leave the CLI showing `⎿ Interrupted · What should Claude do instead?`
with the answer cut mid-word, so the turn really is over; ACT simply is never told.

This matches Claude Code's own documented behaviour for the `Stop` hook, which does not
run when the stoppage was a user interrupt. It is not a bug in the CLI, and no hook
subscription can fix it.

## Why the keystroke, and why that is not "reading the terminal"

With no payload to normalize, ACT has three candidate signals, and two are dead:

- **The transcript.** It records the interrupt as a `user` line whose text is
  `[Request interrupted by user]` (or `… for tool use`) — real structured evidence, and
  the route Codex's `TurnFailed` already uses. Rejected on measurement: for an
  interactive PTY session the file at `transcript_path` **was never written at all**,
  not in 180 s and not after a graceful `/exit`, so the marker would arrive late or
  never. Matching a sentence of someone else's English prose would also be exactly the
  brittleness *ACT never parses terminal output* exists to avoid.
- **The process.** It is alive and would exit zero; nothing about it changed.
- **The keystroke ACT was asked to forward.** Every byte reaching a live agent came
  from the user's xterm through `SessionView.OnData`, so ACT already has the
  observation in its hand.

The third is what ships, and the rule it does not break is worth stating precisely:
*ACT never parses terminal **output***. The interrupt is read off ACT's own **input**
channel — a fact about what the user did, in a keybinding ACT itself documents
(`act-terminal.js`: "Ctrl+C is the interrupt"), not an inference from a screen that
redraws. `PtyAgentTerminal.Input` carries it, `PtyAgentSession` raises
`TurnInterrupted`, and the adapter — not the core — says which keys its CLI honours.

xterm's encoding was read out of the vendored `xterm.js` rather than assumed: with Ctrl
held, keyCode 65–90 becomes `String.fromCharCode(keyCode - 64)`, and keyCode 27 becomes
`C0.ESC` — so Ctrl+C arrives as exactly `0x03` and `Esc` as exactly `0x1b`, one `onData`
call per keypress. The match is therefore on the **whole chunk**, never a substring: a
paste, a dropped file's path, or an arrow key's `ESC [ A` must not be read as an interrupt
because one of its characters is that byte.

## Both keys are wired, and an open picker eats either one

Both keys interrupt. Both report nothing. The difference that matters is not between them at all — it
is **what the composer holds when the key is pressed**, measured a second time after `Esc` support
shipped and put a working card up for review:

| Composer when the key was pressed | Key | Turn actually interrupted? |
|---|---|---|
| empty | `Esc` | **yes** |
| `hello there` | `Esc` | **yes** |
| `hello there` | Ctrl+C | **yes** |
| `/` — command list open | `Esc` | **no**; a second `Esc` then did |
| `/` — command list open | Ctrl+C | **no** |
| `/init ` — the space closed the list | `Esc` | **yes** |
| `look at @` — mention picker open | `Esc` | **no** |

So an **open picker consumes the key before the turn ever sees it**, and it does so for Ctrl+C exactly
as for `Esc` — Ctrl+C was never the safe key, it is only the one nobody presses to close a menu.
Ordinary text in the composer changes nothing: the key still stops the turn. And `/init ` proves the
list closes on a space, so this is about a picker being open, not about the composer being dirty.

## Reporting only the press that could have reached the turn

`TurnInterruptWatch` judges each interrupt keystroke by the keys that came before it: a picker
trigger — `/` or `@`, named by the adapter — arms a single bit of doubt, and the next interrupt key
**spends** it rather than being reported, because that press is the one the picker consumed. The press
after that is reported, which is exactly the sequence the table measured for the command list.

- **The user's report, and now the test:** `/` then `Esc` leaves the card `running` (correct — the
  agent is still working), and the next `Esc` moves it (correct — that is the press that stopped it).
- **Nothing models the composer's *content*.** A caret model over somebody else's line editor is the
  brittleness this whole design exists to avoid — `design-notes.md` records what a model of the CLI's
  *screen* cost — so one bit is all this keeps, and `Enter` clears it because submitting closes
  whatever was open.
- **It is a heuristic, and it is biased on purpose.** When it is wrong it stays **silent** rather than
  lying: a missed interrupt leaves the card claiming `running` until the user's next keypress, which is
  the behaviour that existed before any of this, while a false one moves a card that is still working.

Named ways it goes silent, all of them costing one extra press:

- `/init ` then a key — the space closed the list, so the key really did interrupt, but the doubt is
  still armed.
- A trigger typed and then backspaced away.
- **A pasted or dropped path containing `/`** — including ACT's own dropped-file insertion, which goes
  through the same `WriteAsync`. Left alone rather than special-cased: distinguishing a path from a
  slash command means parsing the composer.

## Why a false positive is the expensive direction

The first version of `Esc` support argued a false positive was cheap because the next activity event
puts the card back. **Live use showed that was too optimistic**, and it is worth writing down: the
turn that got mislabelled was streaming prose, which fires no hook until it ends, so the card sat on
`to review` — blinking for attention, offering **Mark completed**, with its turn count already
incremented — while the agent kept working. Nothing corrected it because nothing was due to arrive.

A missed interrupt fails the other way: the card claims `running` while the CLI is parked, and the
user is standing at the terminal that says `Interrupted`. Both are wrong, but only one of them invites
signing off work that is still being written. That asymmetry is why the watch prefers silence, and why
the rule remains **Executing-only** — a card in Your turn is blocked on something the agent *named*,
and a keystroke may not overwrite that.

## Codex — same profile, reached the hard way

Codex takes **Ctrl+C and `Esc`**, with `/` and `@` as picker triggers: the same profile as Claude
Code. What follows is how this file came to say the opposite for a while, because the mistake is more
instructive than the answer.

| What was pressed | Composer | Result |
|---|---|---|
| Ctrl+C | empty | **turn stops** — the CLI prints `■ Conversation interrupted` |
| `Esc` | empty | **turn stops** — confirmed by a real keypress in a clean session |
| `Esc` | `/` — command popup open | popup closes, turn carries on |
| `Esc` | `@` — file picker open | picker closes (its own footer says *"esc close"*) |
| Ctrl+C | `/` — command popup open | **popup closes, turn carries on** |

### The `Esc` row was measured wrong, twice, and shipped

The first version of this profile named Ctrl+C **alone**, on the strength of a probe that reported the
answer still streaming after one `Esc` and after two — with byte counts attached (96,553 → 97,258 →
98,950 after one press; 106,283 → 108,680 after two). The numbers were real. The conclusion was wrong,
and an interrupted Codex card kept claiming `running` until the user said so.

**What actually happened:** that probe had been driving the same session for several minutes — typing
prompts, opening popups, clearing the composer with `Ctrl+A`/`Ctrl+K`, sending stray backspaces. Codex
binds `Esc` to an **edit-previous-message chord**, and the CLI *said so on its own status line* —
`esc again to edit previous message` — in the very run that produced the numbers. A chord that was
already armed consumed the `Esc` under test. The line was recorded as an *explanation* for why `Esc`
does not interrupt, when it was the evidence that the measurement was invalid.

Two rules follow, and they are the reusable part:

- **Measure a keybinding from a clean session.** One launch, one prompt, one key. A TUI's modes and
  chords are state, and a probe that has been mashing keys is measuring its own leftovers.
- **An injected byte is not a keypress until something proves it is.** Here the encoding really was
  identical — ruled out, not assumed: a CLI that turns on the kitty keyboard protocol or xterm's
  `modifyOtherKeys` would make `Esc` arrive as a *sequence*, so ACT could match the wrong bytes.
  Measured from each CLI's own output, **neither asks for either mode** (Claude Code requests
  `?1049h ?1000h ?1002h ?1003h ?1006h ?2004h ?2031h ?1004h ?9001h` and `CSI >0q`; Codex requests
  `?2004h ?1004h ?9001h ?2026h` — no `CSI > … u`, no `CSI > 4 ; … m`), and the vendored `xterm.js`
  does not implement `?9001` win32-input-mode at all, so it stays plain VT bytes. That check was worth
  running and it was not what was wrong.

So: **the CLI's hint was right all along** (*"esc to interrupt"* on the working line), and the probe
was wrong. The earlier moral drawn here — "a CLI's own hint is not a measurement" — still holds in the
sense that a hint cannot *replace* a measurement, but it is also a signal worth trusting over a probe
that disagrees with it. When the two conflict, suspect the probe.

**Still unmeasured, and it only risks a duplicate:** whether Codex's *hooks* report a turn the user
interrupted. Answering it needs a session whose hooks the user has trusted — the hook-review screen
was answered *"Continue without trusting"* throughout, so no payload could arrive either way. If they
do report it, the keystroke reports the same landing a beat earlier and the hook changes nothing.
## Verified by hand

Against a running ACT on a sandbox store, card #1000 (`claude`, sonnet, `default`), with
the Ctrl+C going in through `SessionView.OnData` the way a keypress does:

| Step | Result |
|---|---|
| Launch, first turn finishes on its own | `Turn ended, ready for review → Your turn`, 08:35:58 |
| A second prompt typed into the terminal | `Activity observed in the terminal → Executing`, 08:37:05 |
| Ctrl+C sent mid-answer at 08:37:26 | `Turn interrupted in the terminal, ready for review → Your turn`, **08:37:26** |

Same second, no polling anywhere in the path. The card's badge read `to review`, its turn
count went 1 → 2, and **Mark completed** appeared — the sign-off is available on the card
the moment it lands, which is the whole point of the column it landed in. The terminal
buffer showed the answer cut mid-word above `⎿ Interrupted`, so the CLI and the board
agreed about what had happened.

Then card #1001, for `Esc` and for the sequence it is one byte away from:

| Step | Result |
|---|---|
| `Esc` sent mid-answer | `Turn interrupted in the terminal, ready for review → Your turn`, 14:36:08 |
| A new prompt typed in | `Activity observed in the terminal → Executing`, 14:36:21 |
| ↑ then ↓ **during the live turn** | **nothing** — badge stayed `running`, no timeline row |
| Ctrl+C on that same turn | `Turn interrupted in the terminal, ready for review → Your turn`, 14:36:35 |

Two interrupt rows in the timeline for two interrupts, and none for the arrow keys. The
CLI's own status line during a live turn reads *"esc to interrupt"*, which is the CLI
agreeing about what the key means.

And card #1002, for the picker sequence the watch exists for — the keys going in through
`SessionView.OnData`, so the whole path is exercised:

| Step | Result |
|---|---|
| `/` typed mid-turn | command list opens on screen, badge stays `running` |
| `Esc` #1 | list closes, badge **stays `running`**, **no timeline row** |
| `Esc` #2 | `Turn interrupted in the terminal, ready for review → Your turn`, 15:20:34 |
| new prompt, then `hold on` typed mid-answer | badge `running`, nothing reported for the typing |
| `Esc` with that ordinary text in the composer | `Turn interrupted … → Your turn`, 15:23:04 |

Two interrupt rows for the two presses that stopped a turn, and none for the press that only
closed a menu — which is the bug this round fixed.

One thing to know when reproducing this: **the folder guard blocks the second card.** A
card holds its working directory until it is *Completed*, so #1001 would not launch in the
same folder while #1000 sat in Your turn — the log says
`Not launched: task 1000 is still working in …`. Sign the first one off, or use a different
directory.

## Probe to re-run on a CLI upgrade

The whole measurement is one console app against a real session; nothing about it needs
ACT running.

1. Write the hook settings a launch would write
   (`ClaudeCodeHookSettings.Compose(endpoint, token)`), point `endpoint` at a local
   listener that logs `hook_event_name` with a timestamp, and answer every hook `200`.
2. Spawn the CLI through `PtyHost` with `AgentEnvironment.For(…,
   ClaudeCodeEnvironmentScrub.Shared, token, endpoint)`, `--session-id`, `--settings`,
   and a positional prompt long enough to still be answering ten seconds later.
3. **Control first:** run it with a one-word prompt and confirm `Stop` arrives. A run
   with no `Stop` in it proves nothing about interrupts.
4. Write `0x03` to the pty mid-answer, then log for **at least three minutes**. Any
   hook at all arriving here is the finding that reverses this file — and if it is
   `Stop`, the keystroke rule can be deleted rather than kept beside it.
5. Repeat with `0x1b`.
6. Confirm the screen shows the interrupt banner, so a run that measured "no hooks"
   is not a run that measured "the key did nothing".
7. **Re-measure the picker table**, which is the half that changed under the first
   version's feet. For each key, drive the composer to a state and then press it, judging
   the result on whether the *banner* appears rather than on anything ACT reports: empty
   composer; ordinary text; `/`; `/` then the key twice; `/init ` with the trailing space;
   `@` mid-sentence. A step probe makes this one command —
   `"wait:10|type:/|wait:3|esc|wait:6|esc|wait:6"`.
8. **Check for a picker trigger this file does not know.** `/` and `@` are the two that
   were found; a CLI that adds a third (or opens the command list on something other than a
   leading `/`) makes `TurnInterruptWatch` report a press the picker ate, which is exactly
   the bug it exists to prevent. The adapter's `TurnInterruptProfile` is where a new one
   goes.
9. **One key per session — this is the step both mistakes broke.** A TUI's chords and modes
   are *state*: Codex's `Esc` was recorded as inert because the probe had spent minutes typing
   into that session and armed its edit-previous-message chord, which then ate the key under
   test. So spawn, send the prompt, send **one** key, judge, and **exit**. A probe that reuses
   a session measures its own leftovers, and it will do it convincingly, with byte counts.
10. **When the CLI's own hint disagrees with the probe, suspect the probe.** The working line
    saying *"esc to interrupt"* was right and the measurement was wrong. A hint cannot replace
    a measurement, but a measurement that contradicts one is a measurement to re-run from a
    clean session — and the cheapest tiebreak of all is asking the user to press the key once
    and say what the agent did.

Two things measured in passing, neither of which ACT depends on:

- **A double Ctrl+C 400 ms apart did not exit** the CLI mid-turn; the first one had
  already interrupted the answer. So the "press Ctrl-C again to exit" path was not
  exercised, and nothing here assumes it.
- **Transcript saving is off for a session launched from inside another Claude Code
  session.** The CLI says so on its own status line — *"inherited
  CLAUDE_CODE_CHILD_SESSION marker"* — and it is why the transcript route above could
  not even be evaluated. ACT's scrub is provably not the channel: a `cmd /c set` spawned
  through `PtyHost` with the scrubbed environment inherits **zero** `CLAUDE*`
  variables. So the CLI is finding the marker some other way, and only an ACT started
  *by an agent* is affected — a normally launched one has no enclosing session to
  inherit from. Worth knowing when a verification run shows empty token and context
  numbers.
