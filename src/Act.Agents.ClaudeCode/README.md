# Act.Agents.ClaudeCode

Claude Code **agent adapter** — a plug-in behind `Act.Core`'s `IAgentAdapter`
port. Self-contained (no shared `Act.Agents.Common` until real shared plumbing
emerges).

Responsibilities:

- **Launch** — spawn `claude` in the task's working dir with a pre-minted
  `--session-id` and the injected preamble.
- **Input channel** — the **stream-json control protocol** over stdin/stdout
  (permission/question control-requests and control-responses). Underdocumented —
  pin the Claude Code version and verify at build.
- **Ingestion sources** — HTTP hooks (lifecycle) + file source (status /
  follow-ups / JSONL enrichment) + process source.
- **Normalized mappings** — `model` / `effort` / `permissionMode` → CLI flags;
  raw hooks → normalized events; unsupported-value behavior (map or reject, never
  silently drop).
