<div align="center">

# ACT — Agent Control Tower

**One board for every coding-agent session on your machine.**

See every Claude Code and Codex session at a glance, know instantly which one
needs you, and stage upcoming tasks to launch as soon as a usage window opens.

</div>

---

## Why ACT exists

Coding agents made it cheap to run multiple tasks at once — and expensive to keep
track of them. Each session lives in its own terminal, and none of them tells
you what the others are doing. The result is a familiar set of problems:

- **You can't see who needs you.** One agent is waiting on a permission prompt,
  another asked a question twenty minutes ago, a third is waiting for your review — and all three
  look like idle terminal tabs until you alt-tab through every one of them.
- **A waiting session is wasted time.** An agent parked on a question sits
  idle until you notice it.
- **Usage windows go to waste.** A token window resets with nothing queued to
  use it, because there is nowhere to stage work in advance — ACT lets you
  prepare tasks ahead and schedules their launch for when your usage window
  becomes available.

ACT is the control tower for that traffic. Every task is a card on a Kanban
board that moves itself through
*Preparing → Ready → Executing → Your turn → Completed* as the session runs. A
badge on each card says exactly why it is where it is: `running`,
`needs permission`, `needs answer`, `error`, `to review`.

The name doubles as the verb *to act*: the tool's whole job is helping you
decide which session needs attention — and act on it.

## Features

- **A live board.** Cards move themselves as the session
  runs, driven by the agent's own lifecycle events.
- **An embedded terminal.**
- **Prepare now, run later.** The board lets you draft and stage as many tasks
  as you want without executing anything right away — launch them yourself, or
  hand them to the scheduler.
- **Scheduling that knows your usage limits.** Queue tasks for *now*, *the next
  5-hour window*, or a specific time. The runner watches your live usage and
  holds the queue when a window is nearly spent, so tasks launch when your
  token window becomes available instead of dying mid-run.
- **Native OS notifications.**
- **Task templates.** Save a recurring shape of work with its prompt skeleton
  and launch config, and create tasks from it in one click.
- **A built-in MCP server — agents spawn tasks themselves.** Every session is
  wired to ACT over MCP, so an agent can create new tasks on the board: a plan
  decomposes itself into implementation tasks.
- **Attachments.** Hand a task screenshots or log files.
- **Timeline and metrics.** Every card keeps a timeline of its transitions and
  live metrics — tokens, context usage, compactions, turns, tool calls.
- **Local-first.** Everything runs on your machine.

## Screenshots

*The board — every session, its state, and whose turn it is:*

![Main board](docs/screenshots/board.png)

*Creating a task — prompt, agent, model, permissions, and schedule in one form:*

![Task creation](docs/screenshots/task-creation.png)

*Inside a task — the agent's real terminal, hosted by ACT:*

![Task terminal](docs/screenshots/terminal.png)

*Every card keeps its full timeline:*

![Task timeline](docs/screenshots/timeline.png)

## Supported agents

- **Claude Code**
- **Codex**

## Supported platforms

**Windows** and **Linux** as a desktop app. macOS is not
supported yet, but the stack is portable and it is open for the future.

## Getting started

Download the installer for your platform from the
[Releases](https://github.com/north-lab-kev/agent-control-tower/releases) page,
run it, and ACT opens as a desktop app. You'll need at least one supported
agent CLI installed and signed in — ACT finds it on its own.

## Documentation

- [docs/overview.md](docs/overview.md) — the full specification.

## Contributing

ACT is **not accepting external contributions right now** — see
[CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and issues are welcome.

## License

**Functional Source License (FSL)** — source-available. Anyone may read, use,
modify, and redistribute the code; only competing commercial use is prohibited.
Each release auto-converts to **Apache 2.0 after two years**. See
[LICENSE.md](LICENSE.md). *Not legal advice.*
