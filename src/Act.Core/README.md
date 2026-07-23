# Act.Core

**Domain + application layer. Depends on nothing infrastructural.** Adapters,
infrastructure, and UI all point *inward* to this project's `Ports/` interfaces —
this dependency direction is the architecture.

Speaks only in **normalized events** and interfaces; must not reference Blazor,
a specific CLI, or the file system directly.

## Subfolders

- `Model/` — `Card`, `Column`, `Badge`, `Transition`, `Lineage`, metrics, launch config…
- `Events/` — the normalized event types the rules engine consumes.
- `Rules/` — the rules engine (pure logic: event in → column/badge out). Highest-value test surface.
- `Scheduling/` — queue runner, `schedule`, `maxConcurrent`, `dependsOn` ordering, backpressure.
- `Ports/` — the interfaces: `IAgentAdapter`, `IIngestionSource`, `ITaskStore`, `INotifier`, `IClock`…
