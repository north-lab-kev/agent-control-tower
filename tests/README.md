# Tests

Fast, deterministic, **no real CLIs**. xUnit + AwesomeAssertions / Shouldly +
NSubstitute. *(Do not use FluentAssertions v8+ — commercial license.)*

- **Act.Core.Tests** — the rules engine & scheduler, tested hard (pure logic,
  ~90%+ coverage target). No processes, files, or agents.
- **Act.Agents.Tests** — contract tests: one shared suite **every** adapter must
  pass, holding Claude Code and Codex to the same normalized behavior.
- **Act.App.UiTests** — Playwright for .NET against the **browser-mode** Blazor
  app (not Electron), driven by the mock adapter. Covers task creation, drag +
  drag-time graying, event-driven column moves, per-state drawer actions, the
  "N need you" jump.
- **Act.TestSupport** — the **mock adapter** + fixtures/builders, shared by both
  the core tests and the Playwright UI tests so deterministic sessions can be
  driven end-to-end.

No automated integration tests against real CLIs (deliberate — slow, flaky,
token-costly); real-CLI behavior is verified manually per roadmap step.
