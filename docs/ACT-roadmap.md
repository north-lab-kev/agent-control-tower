# ACT — Implementation roadmap

Build sequence for ACT. Companion to the spec (`ACT-overview.md`); this file is
build-tracking, not design. Steps are ordered so each is small, independently
verifiable, and leaves something runnable.

## Guiding principles

- **Shell first, release script second** — establish a repeatable release build
  early so every step after stays shippable.
- **Abstraction-first, both agents together** — introduce the agent-adapter seam
  as soon as we touch launching, then implement **Claude Code and Codex in
  parallel** against it. Forces a real abstraction instead of a Claude-shaped one
  (cost: bigger agent-steps; both CLIs needed to test).
- **Observability before automation** — get sessions *visible* and manually
  drivable on the board before adding scheduling / auto-execute / overnight.
- **Spine before breadth** — full end-to-end loop working before polish and
  extra features.

## Phase 1 — Foundation

- [x] **1. Empty app shell** — Blazor Server on .NET 10, runs via `dotnet run`,
  blank page. *Verify:* app loads in browser. ✅ Loads at `http://localhost:5210`
  (title "ACT — Agent Control Tower"), clean build, no console errors.
- [ ] **2. Release build script + CI** — repeatable release build; wire
  Electron.NET so it also produces the desktop artifact; stand up the **test
  projects (xUnit) and GitHub Actions CI** now, so the fast suite runs from step
  one. **While the repo is private:** run **Linux-only CI on push/PR** and the
  **Windows leg occasionally** (pre-release or weekly) to stay inside the free
  2,000-minute tier (Windows drains at 2×); switch on the full Windows + Linux
  matrix at go-public (then free and uncapped). *Verify:* one command yields a
  runnable desktop build; CI is green on an empty test. (Established now so
  everything after ships and is tested.)
- [ ] **3. LiteDB + card model** — persist the full card model; seed fake cards.
  *Verify:* cards survive restart.

## Phase 2 — Board (read-only, agent-agnostic)

- [ ] **4. Static board** — six flat columns, flight strips, compact/spacious
  toggle, rendering seeded cards. Pure UI, no behavior. *Verify:* board reflects
  store; density toggle works.
- [ ] **5. Task creation + manual moves** — new-task modal → card in Preparing;
  drag Preparing ↔ Ready with drag-time graying of invalid columns. *Verify:*
  can hand-manage cards through the human-controlled columns.

## Phase 3 — Agent abstraction + both adapters

- [ ] **6. Agent adapter interface** — define the seam only (no behavior):
  launch, ingestion sources, input channel, the **injected preamble** (teaches
  the agent the `.act/` status + follow-up file conventions — the agent↔ACT
  contract that steps 8–9 and 12 depend on), and normalized mappings
  (`model` / `effort` / `permissionMode`, permission/question events,
  unsupported-value behavior). Ship a **mock adapter** for tests. *Verify:* mock
  adapter drives a fake session through the states.
- [ ] **7. Launch — Claude Code + Codex** — Ready → Executing spawns the agent
  via each adapter with a pre-minted `--session-id` and the **injected preamble**
  (so the agent knows the `.act/` conventions from turn one); bind session →
  card. *Verify:* both real agents launch, bind, and can see the preamble.
- [ ] **8. Ingestion — both adapters** — normalized event stream fed per adapter
  (Claude: control stream + http hooks + files + process; Codex: command
  forwarder + files + process). Card shows live `running` / activity / metrics.
  *Verify:* live state + metrics update for both agents.
- [ ] **9. Rules engine** — agent-agnostic: normalized events → column/badge
  transitions; status-file convention (`ready_for_review` / `needs_input`) routes
  Executing → To review / Needs feedback; `error`/`stale` from process signals.
  *Verify:* a task walks the machine-region states correctly.
- [ ] **10. Drawer + input channel — both adapters** — contextual drawer; answer
  questions, approve/deny permission, send-back, kill, retry — each adapter's
  input-channel implementation (Claude: stream-json control protocol; Codex: its
  own). *Verify:* a task goes full-circle by hand on both agents.

## Phase 4 — Completion & lineage

- [ ] **11. Complete + reopen + auto-complete** — To review → Completed and
  reopen; `autoComplete` and in-session `autoGit`. *Verify:* clean-finish tasks
  self-complete; reopen resumes.
- [ ] **12. Spawning & lineage** — `.act/followups/` ingestion, parent/children,
  the git-on-completion spawned task. *Verify:* a task spawns tracked follow-ups
  with correct lineage.

## Phase 5 — Automation (on a proven base)

- [ ] **13. Native notifications** — OS pings for attention states; actionable,
  focus-aware, coalesced. *Verify:* backgrounded ACT pings on needs-you.
- [ ] **14. Scheduling & queue runner** — `schedule`, `maxConcurrent`,
  `dependsOn` ordering, rate-limit backpressure (`waiting-reset`), keep-awake,
  and the global **auto-execution pause switch**. *Verify:* a queue of Ready
  tasks processes unattended across a window reset; pause halts all auto-launch.

## Phase 6 — Breadth & polish

- [ ] **15. Persistence polish** — auto-archive (90-day default), search,
  transitions timeline in the drawer. *Verify:* old Completed cards archive and
  stay searchable.
- [ ] **16. UI polish** — final spacing, type, Radzen theming pass.
  - **Settings surface** — consolidate the preferences that landed scattered
    across features into one screen: density default, notification matrix,
    keep-awake, `maxConcurrent`, weekly-reset time, auto-archive window,
    auto-execution pause. (Each knob works from its own feature step; this just
    gives them a home.)

## Milestones

- **After step 10** — usable, manually-driven, **multi-agent** ACT (a plausible
  v1).
- **After step 14** — the overnight unattended batch vision.
- **After step 16** — the full, polished product.

## Build-time items to verify (from the spec)

- Electron.NET `--settings`-style hook injection flag (no clobbering user config).
- Claude Code stream-json control protocol details (underdocumented — pin the
  version).
- `/usage` scriptability + reset-time detection; clean mid-task resume after a
  rate-limit interruption.
- Codex equivalents for: input channel, permission events, `permissionMode`
  mapping, and whether it has a stream-control channel at all.

## Go-public checklist (when ready)

Independent of the build phases — do whenever you decide to flip the repo public.

- [ ] **No secrets in history** — the whole commit history becomes visible on
  flip. From day one: `.gitignore` for `.env`/keys, use GitHub Actions secrets,
  never commit the per-session endpoint token or any API key. (Scrubbing history
  later is painful — prevent, don't fix.)
- [ ] **License in place from the first commit** — `LICENSE.md` (FSL) with your
  name + change date committed early, so there's no ambiguous unlicensed period.
- [ ] **Third-party NOTICES file** present (permissive-dependency attribution).
- [ ] **Flip visibility** — repo Settings → change visibility → public.
- [ ] **Enable the full CI matrix** — turn on Windows + Linux on every push/PR
  (free and uncapped once public).
