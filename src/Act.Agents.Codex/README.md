# Act.Agents.Codex

Codex **agent adapter** — a plug-in behind `Act.Core`'s `IAgentAdapter` port.
Self-contained; held to the same normalized behavior as the Claude Code adapter
via the shared contract-test suite.

Responsibilities:

- **Launch** — spawn `codex` under the pseudo-terminal with the task text as its
  positional prompt and ACT's `--profile` layered over the user's config. No
  input channel: the user types in the embedded terminal.
- **Observers** — process signals today; the session rollout tail (binding,
  activity, turn end, enrichment) is **not written yet** — step 8 items 6–7. Its
  hooks do not fire on the pinned CLI, so the command-hook forwarder is generated
  but dormant — see `docs/codex-hooks-findings.md`.
- **Normalized mappings** — `model` / `effort` / `permissionMode` → Codex's real
  settings; raw events → normalized events.
