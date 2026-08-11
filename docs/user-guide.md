# ACT User Guide

Everything you need to know to use ACT — what it does, how to work with it,
and what the settings mean. No prior knowledge required. For the full
technical specification, see [overview.md](overview.md).

---

## What is ACT?

ACT (Agent Control Tower) is a desktop app that puts every coding-agent
session on your machine — Claude Code and Codex — on one Kanban board. Each
task is a card that moves across the board on its own as the agent works, so
you always know which session is running, which one is waiting for you, and
which one is done.

ACT never acts on the agent's behalf: it watches, reports, and hands you the
terminal when it's your turn. You stay in control of every answer, every
permission, and every sign-off.

## Getting started

1. Download the installer for your platform (Windows or Linux) from the
   [Releases](https://github.com/north-lab-kev/agent-control-tower/releases)
   page and run it.
2. Make sure at least one supported agent CLI (Claude Code or Codex) is
   installed and signed in. ACT finds it automatically — no configuration
   needed in the common case.
3. Open ACT. You'll see an empty board with five columns. Create your first
   task with the **+** button in the *Preparing* column.

## The board

The board has five columns. A card travels left to right:

| Column | What it means | Who moves the card |
|---|---|---|
| **Preparing** | You're still drafting the task. | You |
| **Ready** | The task is queued, waiting to launch. | You (or the scheduler launches it) |
| **Executing** | The agent is working. | ACT, automatically |
| **Your turn** | The agent needs something from you. | ACT, automatically |
| **Completed** | You signed off on the work. | You (drag it there from *Your turn*) |

Once a task launches, ACT moves it automatically — you can't drag a card
that's mid-flight. The two moves that stay yours are the sign-off (*Your turn
→ Completed*) and the reopen (*Completed → Your turn*).

### Badges — why a card is where it is

Every launched card carries a badge that says what's happening right now:

- **running** — the agent is working.
- **needs permission** — the agent is waiting for you to approve something.
- **needs answer** — the agent asked you a question.
- **error** — the session crashed or failed.
- **to review** — the agent finished; the work is waiting for your sign-off.
- **compacting** — the agent is condensing its context (brief, automatic).

Cards that need you glow and pulse so they stand out at a glance: amber for a
blocked prompt, red for an error, blue for work ready to review.

## Creating a task

Click **+** in the *Preparing* column. The task form asks for:

- **Title** — optional to type. Leave it blank and ACT names the task from
  your prompt automatically.
- **Prompt** — the instruction the agent will receive. This is the heart of
  the task and can't be changed after launch.
- **Working directory** — the folder the agent works in. Type a path or
  browse for one; ACT warns you if it doesn't exist and offers to create it.
- **Agent, model, and effort** — which CLI runs the task and with what
  settings.
- **Permission mode** — how much freedom the agent gets, from asking about
  everything to running fully unattended. For overnight runs, pick a mode
  that never prompts, or the task will sit waiting for an answer nobody is
  awake to give (the form warns you about this).
- **Schedule** — when the task may start (see *Scheduling* below).
- **Git when done** — optionally ask the agent to commit, push, or open a
  pull request as part of its work.

### Attachments

Hand a task files — screenshots, logs, specs — by picking, dragging, or
pasting them into the form (up to 10 files, 25 MB each). The agent receives
them alongside the prompt. You can also drop a file straight onto a running
task's terminal.

### Templates

If you create the same kind of task often, save it as a template (*Save as a
template* in the task form's footer). A template remembers the whole form —
prompt skeleton, agent, folder, settings — and the **+** button grows a menu
to create tasks from it in one click. Manage templates from the bookmarks
icon in the top bar.

## Launching and scheduling

Drag a card from *Preparing* to *Ready* when the prompt is final. From there
it launches one of two ways:

- **Launch now** — press the button on the card and it starts immediately.
- **Automatically** — set a schedule and let ACT start it for you:
  - **Manual** (default) — never auto-launched; you start it by hand.
  - **Now** — as soon as possible.
  - **Next 5-hour window** — when your usage window next resets. This is how
    you stage work in the evening and have it run overnight.
  - **Specific date & time** — exactly when you say.

The scheduler is careful on your behalf:

- It launches Ready cards **top to bottom** — drag a card to the top of the
  column to say "this one next".
- It won't run more than a set number of tasks at once (default 5).
- It **watches your usage limits** and holds the queue when a window is
  nearly spent, so a task launches after the reset instead of dying mid-run.
- It won't start two tasks in the **same folder** at the same time, so agents
  never overwrite each other's work.
- A held card shows a small chip explaining why it's waiting (`queued`,
  `usage`, `folder`, `paused`, …) and starts on its own when the hold clears.

A **pause switch** on the board stops all automatic launches at once — handy
when something is going wrong. Manual launches still work while paused.

## When it's your turn

A card lands in *Your turn* whenever the agent needs you: a permission
prompt, a question, an error, or finished work to review. Click the card (or
the OS notification) and you land in the **session view** — the agent's real
terminal, full screen, exactly as if you'd run it yourself. Type your answer,
grant the permission, or read the result.

- Answering in the terminal sends the card back to *Executing*
  automatically.
- **Retry** relaunches a task that errored, resuming where it left off.
- Drag the card to **Completed** when you're satisfied — this is your
  sign-off, and it ends the session.
- Changed your mind? Drag it back (**reopen**) and pick up the conversation
  where it ended.

Nothing completes itself: ACT never decides work is done — you do.

## Follow-up tasks

Agents can create tasks too. A planning task can break its plan into
implementation tasks, each landing in *Ready* as a new card. Spawned cards
default to manual launch so you always review them before they run, and every
card shows where it came from, so you can trace a task back to its parent.

## Staying informed

- **OS notifications** tell you the moment a task needs you or finishes —
  the "it's done" ping for overnight runs. Clicking one takes you straight
  to that task's terminal.
- **Usage meters** in the top bar show each agent's live usage — percentage
  used and time until the window resets — the same numbers the scheduler
  acts on.
- **Close to tray** keeps ACT (and your running agents) alive when you close
  the window; the tray icon's tooltip counts the tasks waiting for you.

## Finding past work

- **Filter boxes** on the board and the archive narrow what's shown as you
  type — matching a task's number, title, folder, or prompt.
- **Completed tasks auto-archive** after a configurable number of days
  (default 10). Nothing is lost: the archive keeps every task with its full
  history, restorable in one click.
- **Delete is always soft** — a deleted card goes to the archive, which is
  the undo. The only permanent action in ACT is emptying the archive itself.
- **Duplicate** any task — live or archived — to run the same work again as
  a fresh card.

## Settings

Open Settings from the gear in the top bar. Every change applies immediately
— there's no OK button. The highlights:

- **General** — language (English / French, follows your OS by default).
- **Appearance** — theme (dark / light / follow OS), board density
  (Compact / Detailed), and the attention blink.
- **Execution** — pause automatic execution, how many tasks run at once,
  the usage threshold that holds the queue, and the one-task-per-folder
  guard.
- **Retention** — whether and when completed tasks auto-archive.
- **Agents** — one section per agent CLI: enable/disable it, and point ACT
  at the executable if it isn't found automatically.
- **System** — desktop notifications, keep the computer awake while tasks
  run (so overnight queues don't stall when the machine sleeps), and close
  to tray.
- **Updates** — how ACT updates itself: quietly, on request, or never. Also
  shows the version you're running.
- **Diagnostics** — anonymous usage reporting (on by default, one switch to
  opt out) and the log folder.

## Where your data lives

Everything is local. Tasks, history, and attachments live in a single data
folder on your machine (`%LOCALAPPDATA%\ACT` on Windows,
`~/.local/share/ACT` on Linux) and survive upgrades and reinstalls. ACT
never writes into your project folders.
