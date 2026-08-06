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
  Playwright for .NET against the **browser-mode** app (not Electron) is still the
  plan for the end-to-end pass, and its case is now narrow: the five JS interop
  modules, which bUnit stubs and therefore cannot see at all.
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
