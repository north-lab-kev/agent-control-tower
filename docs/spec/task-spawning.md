# Task spawning & lineage

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

Tasks can be created two ways: **manually** (born in Preparing) or **spawned**
by another task (born in Ready, prompt already defined). It is a general
mechanism — **any task can spawn follow-up tasks** (emergent; there is no special
"plan" task type). A plan task decomposing into implementation tasks and a
reviewed task handing its leftovers to a second card are the *same* mechanism.

## Lineage

Every card carries:

- `origin` — `manual` or `spawned` (with `parentId` when spawned)
- `children[]` — the tasks this task spawned

So navigation works both directions: a parent lists its children; a child shows
where it came from. Spawn **author** is also recorded, since it affects labeling:

- **ACT-emitted** — deterministic; ACT writes the child's prompt itself.
- **Agent-emitted** — the agent produces the follow-ups (e.g. plan → tasks).

**Every spawn today is agent-emitted** — git rides the prompt rather than spawning
a task (see *Git integration*). The field stays because the distinction is about
labeling and would be needed the day ACT writes a child's prompt itself.

## Spawned tasks land in Ready

Their prompt is already defined, so they skip the human drafting step — but they
enter the **Ready queue**, so the user still gates the launch and may send one
back to Preparing to tweak its prompt first. The launch-boundary rule is intact:
a completing task **creates** cards in Ready, it never **moves** an existing
card (creation ≠ a manual column move).

## Spawn mechanism

**ACT-emitted spawns** skip all of the below — ACT already has the prompt, so it
creates the Ready card **directly in its own store**. No disk round-trip. Nothing
emits one today.

**Agent-emitted spawns** call the **`create_followup` MCP tool** — the whole
agent→ACT contract, described in *Agent ↔ ACT contract*.

- **Arguments:** the task (`title`, `prompt`), where it runs (`cwd`, defaulting to the
  parent's), how it runs (`agent`, `model`, `effort`, `permission`, `autoGit` — each
  defaulting to `same`, meaning the parent's value), when it runs (`schedule`), and
  `dependsOn`. The full table and the reasoning for every inclusion and omission are in
  *Agent ↔ ACT contract*.
- **The parent is the token, not an argument.** ACT resolves the calling session
  from the per-session token, so an agent can only spawn onto its own card.
- **ACT still owns id assignment.** Each child gets a freshly minted UUID and
  `number`, and the call **returns** them, which is what lets `dependsOn` name real
  ids instead of transport handles.
- **Ingested immediately, not at `Stop`.** The card lands in Ready when the tool is
  called; there is no scan trigger and no consumed directory, so ingestion is
  idempotent by construction — one call, one card.
- **Capped at 100 per parent for the card's lifetime**, counted off `children[]` and
  configurable under *Settings → Advanced*.
- **The parent's timeline records each spawn.** One `spawnedFollowUp` transition per
  child, carrying the child's number and title in its note and no column change — the
  parent did not move, it produced something.

`dependsOn` is the seed of task ordering (a plan implies sequence); ordering is
enforced by the scheduling runner — a task launches only after its `dependsOn`
prerequisites are Completed. It may name **any** card, not only siblings the same
session created, which is what the `list_tasks` read tool exists to make discoverable.
