# Act.Agents.Codex

Codex **agent adapter** — a plug-in behind `Act.Core`'s `IAgentAdapter` port.
Self-contained; held to the same normalized behavior as the Claude Code adapter
via the shared contract-test suite.

Responsibilities:

- **Launch / input channel** — Codex implements the input channel its own way
  (its explicit `PermissionRequest` event, etc.); whether it has a stream-control
  channel at all is a build-time item to verify.
- **Ingestion sources** — command-hook forwarder + file source + process source.
- **Normalized mappings** — `model` / `effort` / `permissionMode` → Codex's real
  settings; raw events → normalized events.
