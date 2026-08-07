# Tests

Fast, deterministic, **no real CLIs**. xUnit + AwesomeAssertions / Shouldly +
NSubstitute. *(Do not use FluentAssertions v8+ — commercial license.)*

- **Act.Core.Tests** — the rules engine & scheduler, tested hard (pure logic,
  ~90%+ coverage target). No processes, files, or agents.
- **Act.Agents.Tests** — contract tests: one shared suite **every** adapter must
  pass, holding Claude Code and Codex to the same normalized behavior.
- **Act.App.UiTests** — the app layer: its services, and a **bUnit** suite per
  component. No web server and no browser — bUnit renders in-process and asserts
  over an AngleSharp DOM, so the whole project runs in about three seconds.
- **Act.App.E2eTests** — **Playwright** against the real app on a real port,
  driven by the mock adapter. Deliberately narrow, because bUnit covers the
  components: the JS interop modules, xterm, and a handful of round trips. **Run
  by hand** — see *Browser tests* below.
- **Act.TestSupport** — the **mock adapter** + fixtures/builders, shared by both
  the core tests and the Playwright UI tests so deterministic sessions can be
  driven end-to-end.

No automated integration tests against real CLIs (deliberate — slow, flaky,
token-costly); real-CLI behavior is verified manually per roadmap step.

## Component tests

**Extraction beats rendering.** If a fact is `Card` + `now` in, strings out, it
belongs in a pure class with a pure test — the way `StripFace` was pulled out of
`FlightStrip`, which is why `StripFaceTests` covers twenty cases while
`FlightStripTests` needs seven. A render test for what a switch expression returns
is strictly worse than a plain one, and a page test that starts wanting to assert a
computed string usually wants a new `Cards/` type instead.

So a bUnit test earns its place only by asserting one of:

- a computed value reaching the element that consumes it (class, `title`, `href`,
  `disabled`),
- a gesture reaching the right handler — including bubbling and `stopPropagation`,
- conditional markup: which branch rendered, and what is **absent**,
- an `EventCallback` contract between a parent and a child,
- lifecycle: what a re-render, a parameter change or a service event does.

### The harness

`ComponentTest` is the base class. It builds the app's own service graph over the
fakes this project already had — deliberately **not** `AddActApp`, which wants an
`IConfiguration`, a data directory and a web host. What is shared with production is
the wiring shape, not the composition root. It also pins the culture to `en`, freezes
the clock, and seeds the two agent defaults that `AgentInstallDiscovery` writes at
startup — without which `EnabledAgents` is empty and every page offers no agent, a
state the real app never reaches.

Four things about it are worth knowing before writing a new suite:

- **It is `IAsyncLifetime`.** `SessionRegistry` and `QueueRunner` are
  `IAsyncDisposable`-only, and the container refuses a synchronous teardown while it
  owns one.
- **A page under test renders no `RadzenComponents`**, so a toast is read off
  `NotificationService.Messages` and a dialog off `DialogsOpened` / `AnswerDialog`
  rather than found in the markup. `OpenDialog` is the other direction: it renders
  Radzen's host so a dialog component can be driven end to end.
- **`RadzenDom` names Radzen's rendered shapes** — a control is found by the words
  beside it, and a dropdown renders its whole item list into the DOM whether the
  panel is open or not, which is what lets a test read and change one without driving
  a popup. Everywhere else, selectors are the app's own classes; `.rz-*` belongs in
  that one file.
- **The second mock adapter is narrower than the first** (`NarrowerCapabilities`).
  Two adapters with identical capabilities can never show that switching agent drops
  a model the new one does not offer, which is the rule `OnAgentChanged` exists for.

## Browser tests

`Act.App.E2eTests` is **excluded from the default run** — `scripts/build.ps1` filters on
`Category!=E2E`, and `-E2E` runs only that category. It needs Chromium on the machine and takes
about a minute; the fast suite takes seconds and must stay that way.

In CI it runs as its own `e2e` job, which builds, installs Chromium, then calls
`./scripts/build.ps1 -E2E -NoBuild`. Locally:

```powershell
# once per machine
tests/Act.App.E2eTests/bin/Debug/net10.0/playwright.ps1 install chromium

# then
./scripts/build.ps1 -E2E
```

Set `ACT_E2E_HEADED=1` to watch it run.

### What it exists for

Three things, and nothing else — everything a render can answer belongs in bUnit:

1. **The JS interop modules.** bUnit stubs JS, so a component test can prove the page *asked* for a
   module and never what the module did. `act-attach` alone is 214 lines of measured drag and
   clipboard behaviour.
2. **xterm.** It needs a real window to paint.
3. **Round trips.** The circuit, the store on disk, routing, Radzen's JS half, and the push path —
   a card moving on screen because an agent event arrived, with nobody touching the browser.

What it does **not** prove is the pty: the mock adapter hands back a mock session, so no process is
spawned and `PtyHost` is never touched. Real-CLI behaviour stays manual, per each roadmap step's
*verify* line.

### Two rules

- **Never a real agent.** Every session here goes through `MockAgentAdapter`. A spec that would need a
  real CLI is not written — it is reshaped onto the mock or dropped. That is the project's existing "no
  automated integration tests against real CLIs" rule applied to the browser tier, and it is also what
  lets the suite run on a machine with neither CLI installed. Why the mock reaches the *real* host the
  way it does, and the two alternatives that were rejected, are in `docs/design-notes.md`.
- **Never sleep to wait for the app.** `AgentScript.AwaitsKeystroke()` parks the scripted session until
  the input really arrives, so a terminal round trip synchronises itself, and Playwright's assertions
  auto-wait on the DOM. A spec reaching for a timer has not found the real signal yet. The one exception
  is asserting that something *did not* happen, where a short wait is the only way to give the wrong
  answer a chance to appear.

### The two things that will bite you

- **`ActApp` starts two hosts.** `WebApplicationFactory` insists on owning a `TestServer`, which has
  no socket a browser can reach, so the builder is built twice — a shadow host for the base class and
  a Kestrel host for Playwright. They cannot share a data directory, because LiteDB takes an
  exclusive lock on `act.db`. Everything a spec asks of the app goes through `App`, the *Kestrel*
  container; `Services` is the other one.
- **A JS module binds later than the page paints.** It is imported in `OnAfterRenderAsync`, so a spec
  that acts the moment the markup appears will race it and lose often enough to be useless. Use a
  real gate — `Clipboard.WaitForReadyAsync` dispatches a drag until the module answers — never a
  sleep. `AgentScript.AwaitsKeystroke()` does the same job on the agent side: the scripted session
  parks until the input really arrives, so a terminal round trip synchronises itself.

The same rule applies to anything the *browser* does asynchronously. `ThemeTests` reads a computed
colour after switching `prefers-color-scheme`, and the style recalc lands a frame or two later — read
straight afterwards and you get the value from before the switch. That one passed in Debug and failed
in Release purely on timing; it now waits for the value to change.
