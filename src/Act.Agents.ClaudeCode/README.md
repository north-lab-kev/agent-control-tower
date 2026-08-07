# Act.Agents.ClaudeCode

Claude Code **agent adapter** — a plug-in behind `Act.Core`'s `IAgentAdapter`
port. Self-contained (no shared `Act.Agents.Common` until real shared plumbing
emerges).

Responsibilities:

- **Launch** — spawn `claude` under the pseudo-terminal in the task's working dir
  with a pre-minted `--session-id`, the task text as its positional prompt, and a
  generated `--settings` file carrying the hook config.
- **Input channel** — none. The user types in the embedded terminal; ACT observes
  and never answers a prompt.
- **Observers** — HTTP hooks (lifecycle) + process signals, pushed into
  `IAgentEventSink`. The JSONL transcript tail (enrichment) is **not written yet**
  — step 8 item 4.
- **Normalized mappings** — `model` / `effort` / `permissionMode` → CLI flags;
  raw hooks → normalized events; unsupported-value behavior (map or reject, never
  silently drop).
