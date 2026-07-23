# Contributing to ACT

*Placeholder — to be expanded.*

## Ground rules

- Respect the architecture: `Act.Core` depends on nothing below it. Adapters,
  infrastructure, and UI depend on Core's `Ports/` interfaces — never the
  reverse. Adding an agent must not touch core logic.
- Use the spec's **domain vocabulary** verbatim in code (cards, columns, badges,
  transitions, adapters, sources) so spec and code stay legible together.
- Nullable reference types on; warnings-as-errors on the core projects;
  `dotnet format` enforced.
- Tests: xUnit + AwesomeAssertions / Shouldly + NSubstitute. **Do not use
  FluentAssertions v8+** (commercial license).

## CLA / DCO

Contributions are accepted under a Contributor License Agreement / Developer
Certificate of Origin (protects future dual-licensing). *Exact mechanism TBD.*

See [docs/ACT-overview.md](docs/ACT-overview.md) for the spec and
[docs/ACT-roadmap.md](docs/ACT-roadmap.md) for the build sequence.
