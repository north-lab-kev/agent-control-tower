# Act.Infrastructure

Concrete implementations of `Act.Core`'s infrastructure ports:

- **LiteDB store** — `ITaskStore` (embedded, single-file document DB).
- **Localhost hook host** — the HTTP ingestion endpoint (localhost-only, random
  port, per-session token; accept-and-return-instantly, process asynchronously).
- **FileSystemWatcher** — the file source (`.act/status/`, `.act/followups/`,
  JSONL transcript enrichment; atomic-write + consume-on-ingest conventions).
- **Process supervision** — spawning, watchdog (`stale`), exit-code (`error`).
- **Serilog wiring** — structured logging with a per-session correlation id
  (`sessionId`).
