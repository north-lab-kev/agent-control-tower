# Act.Agents.Codex

Codex **agent adapter** — a plug-in behind `Act.Core`'s `IAgentAdapter` port.
Self-contained; held to the same normalized behavior as the Claude Code adapter
via the shared contract-test suite.

Responsibilities:

- **Launch** — spawn `codex` under the pseudo-terminal with the task text as its
  positional prompt and ACT's `--profile` layered over the user's config. No
  input channel: the user types in the embedded terminal.
- **Observers** — command hooks forwarded to ACT's loopback endpoint (lifecycle,
  activity, `PermissionRequest`), the session rollout tail (enrichment: tokens,
  context, the last message), and process signals. Verified end to
  end; the long detour to get there — the hooks fire, but ACT was quoting
  the program token, so every one failed — is in
  `docs/findings/codex-hooks.md`, which is worth reading before touching the
  hook config.
- **Normalized mappings** — `model` / `effort` / `permissionMode` → Codex's real
  settings; raw events → normalized events. Note the adapter declares **five**
  permission modes rather than six: Codex has no classifier tier, so `auto` is
  not offered.
