# ACT — Agent Control Tower

The authoritative spec, split into one file per area under [`spec/`](spec/).
Read them in this order — each builds on the ones before it. Italicised
cross-references inside the files ("see *Rules engine*") name these sections.

| Spec file | What it defines |
|---|---|
| [State model](spec/state-model.md) | Columns, badges, the control arc, the launch boundary, column ordering. |
| [Task spawning & lineage](spec/task-spawning.md) | Follow-up tasks, `parentId`/`children[]`, `dependsOn`, the spawn cap. |
| [Task / card data model](spec/data-model.md) | Every card field, launch config, task templates, attachments, auto-generated titles. |
| [Ingestion & session identity](spec/ingestion.md) | Sources, normalization, session binding, the hook endpoint's role. |
| [Rules engine](spec/rules-engine.md) | The transition table, error handling and retry, the quiet chip, the Agent ↔ ACT contract (MCP). |
| [Liveness & the embedded terminal](spec/terminal.md) | The pty, the input channel, restart, desktop handoff. |
| [Scheduling & queue policy](spec/scheduling.md) | Per-task schedule, the runner, usage backpressure, the usage indicator, keep-awake. |
| [Persistence & archival](spec/persistence.md) | Retention, the archive, session restore, the single-instance rule. |
| [Native OS notifications](spec/notifications.md) | Triggers, focus rules, one-state-one-ping, toast identity. |
| [UI direction](spec/ui-direction.md) | The control-room aesthetic, flight strips, densities, interaction rules, settings, localisation. |
| [Local-endpoint security](spec/local-endpoint-security.md) | The kept hook port, per-session tokens, `/hooks/*`, `/mcp`, the attachments route. |

What ACT *is* and why is the [README](../README.md)'s job; how to use it is the
[user guide](user-guide.md)'s; how to build and test it is
[development.md](development.md)'s; how the non-obvious decisions were reached is
[design-notes.md](design-notes.md)'s.

---

## Spec status

The design is **complete and built** — everything this spec describes is
implemented and verified, on both agents. What remains before a first public
release is release engineering, tracked in `docs/todo.md`.

The liveness model rests on the evidence of the launch prototype (branch
`prototype`, commit `bb7633d`), which built the candidate routes side by side:
ACT hosts the agent's **real interactive TUI** under a pseudo-terminal instead of
driving a headless stream-json session, hooks are a **read-only** observability
channel, and `claude://resume?session=` is a confirmed per-card action.

---

## Known constraints to carry forward ⚠️

- CLI-launched sessions do not appear in the Claude desktop app's own session list
  (separate stores) — but `claude://resume?session=<uuid>`
  **imports one on demand**, which is the bridge ACT uses. ACT is still the
  visibility surface. Prefer explicit `--session-id` / `--resume <id>` over
  `--continue`.
- **A live card is a live process.** ACT holds one agent process per active card
  for the card's whole active life, and they all die when ACT does. Bindings survive
  in LiteDB; terminal scrollback does not.
- **The TUI is the API.** Hosting the real interface means ACT inherits its
  quirks — resize behaviour, bracketed paste, ANSI rendering — and inherits changes
  to it on every CLI release. Pin the Claude Code version and re-verify on bumps.
  The mitigation is that ACT reads *none* of it: the blast radius of a TUI change is
  the terminal looking different, never ACT misreading a state.
- Electron.NET no-CLI path is pre-release; Node.js 22.x needed on the build
  machine; Linux builds from Windows need WSL2.

---

## Future enhancements 🚀

- **Headless unattended mode** — the rejected stream-json control protocol, brought
  back for the one case where its objection does not apply: a scheduled overnight
  task has no human to hand a terminal to, so there is no interactive session to
  preserve and no prompt surface worth re-implementing (a non-prompting
  `permissionMode` means it should never prompt anyway). Would run as a second
  session kind behind the same `IAgentAdapter` seam, with no `IAgentTerminal`.
  Only worth building if PTY-hosted overnight runs prove wasteful in practice.
- **Embedded git** — instead of the user asking for it in the prompt and the agent
  doing it in-session, ACT runs the git / host-CLI operations **itself** to save
  tokens (a commit is deterministic work not worth spending LLM tokens on). ACT
  deliberately has no git feature today: a commit, a push or a PR is one sentence
  in the prompt, which the user writes better than a dropdown does — see *Agent ↔
  ACT contract* for why ACT appends nothing of its own.
- **Remote access** — check in on the board from a phone while away (the overnight
  use case begs for it): at minimum a read-only view of which cards need you.
  Answering remotely means shipping the *terminal* to the phone (xterm.js already
  runs in a browser, so the pieces exist) rather than building the approve/deny
  surface ACT deliberately does not have. Security-sensitive — needs auth and a
  safe transport, and a remote terminal is a remote shell, so the bar is high.
- **Recurring / cron tasks** — schedule a task to run on a repeating cadence (e.g.
  "every morning, run the dependency-update task"). Small leap from the existing
  scheduler, which already understands time.
- **Git worktree support** — let a card run in its own git worktree so two cards on
  the same repo cannot fight over the same files. Claude Code supports worktrees
  natively (ACT would pick the location, pass it as the working directory, clean up
  on completion); Codex has no native support and would need an external wrapper
  ACT cannot assume is installed — a probe plus a clear "unavailable" path. Deferred
  because the two adapters diverge sharply; revisit once per-card working
  directories feel limiting.
- **macOS builds** — the stack is portable, but shipping means adding a `mac`
  target, building on a `macos-latest` runner (bills at 10× while private), and
  Apple signing + notarization to avoid Gatekeeper warnings. Deferred until there
  is demand; the stated floor is Windows + Linux.
