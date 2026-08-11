# Git integration

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

**`autoGit` is the whole of it, and it is a prompt suffix.** The task form offers an
optional action — **commit**, **push**, **create PR** (with a **draft** checkbox) —
chosen at creation; `AutoGitInstruction` appends one sentence to the prompt at launch
and the agent does the work **in-session**. Nothing is spawned, nothing is asked at
sign-off, and ACT runs no git of its own. See *Agent ↔ ACT contract* for why appending
it is not ACT teaching the agent a convention.

**There is deliberately no completion modal and no spawned git task** — no sign-off
popup asking for a git action, no ACT-emitted git card in Ready to run one:

- **The suffix already covers the case.** Git known at creation is the normal one, and
  it costs no card, no second launch and no modal.
- **A modal on the sign-off taxes every completion for a minority of them.** Completion
  is the one thing ACT asks of the user; putting a question in front of it makes the
  gesture heavier every single time, and *Interaction* holds that dialogs are for forks,
  not for places.
- **At review time the terminal is already open.** A user who decides on git while
  reading the work types it into the session in front of them — the same live TUI they
  reviewed in — or drags out a follow-up task. Neither needs a mechanism.
- **A spawned card would be a second session for deterministic work**, which is the
  objection *Embedded git* under *Future enhancements* already records.
