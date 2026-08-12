# Act.Infrastructure

Concrete implementations of `Act.Core`'s infrastructure ports:

- **LiteDB store** — `ICardStore` / `ISettingsStore` (embedded, single-file document DB),
  including the tolerant mapping that keeps a card written by an older build loadable.
- **Localhost hook host** — the HTTP ingestion endpoint (loopback-only, kept
  port, per-session token; accept-and-return-instantly, process asynchronously).
- **Transcript reader** — `ITranscriptReader`: reads whole lines out of a JSONL the agent
  still holds open, from a stored offset. Polled by the app layer, never watched.
- **Pseudo-terminal** — `IPtyHost` over `Porta.Pty`: spawn, decode, scrollback, resize, kill.
- **Process supervision** — spawning and exit-code (`error`). No idle watchdog: silence is
  not a state ACT claims (see the spec's *No stale badge*).
- **Serilog wiring** — structured logging with a per-session correlation id
  (`sessionId`).
