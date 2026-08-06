using Act.Core.Model;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// One app, one browser, one page, per test class. Not per test: starting Kestrel and Chromium costs
// more than every assertion in this project put together, and the app is cheap to reset by writing the
// board a spec wants before it boots.
//
// Headless by default, `ACT_E2E_HEADED=1` to watch it. Slow-motion comes with the headed flag, because
// the only reason to watch is to see what is going wrong.
[Trait("Category", "E2E")]
public abstract class BrowserTest : IAsyncLifetime
{
    private IPlaywright? playwright;

    private IBrowser? browser;

    internal ActApp App { get; } = new();

    internal IPage Page { get; private set; } = default!;

    // Overridden by a spec that needs a board before the first paint. Runs before the app boots.
    protected virtual void Arrange()
    {
    }

    public async Task InitializeAsync()
    {
        Arrange();

        // Touching `Url` is what builds and starts the host — the factory is lazy, so this has to come
        // after `Arrange`.
        var url = App.Url;

        playwright = await Playwright.CreateAsync();

        var headed = Environment.GetEnvironmentVariable("ACT_E2E_HEADED") == "1";

        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
            SlowMo = headed ? 250 : 0,
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = url,
            ViewportSize = new ViewportSize { Width = 1400, Height = 900 },
        });

        Page = await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (browser is not null)
            await browser.DisposeAsync();

        playwright?.Dispose();

        App.Dispose();
    }

    // The circuit has to exist before a click means anything. A prerendered page renders identical markup
    // and answers no events at all — a click lands on it, does nothing, and the assertion times out
    // pointing at the wrong thing entirely.
    //
    // **All three waits are load-bearing; none of the first two is enough on its own.** The socket says
    // the server is reachable. `HandlersWired` says this page can actually receive a click: Blazor stamps
    // a `_bl_<guid>` attribute on every element it has bound a handler to, and only when the circuit
    // renders. Removing it brings back a one-in-six flake in which clicks vanish without a trace — see
    // *Knowing a Blazor page is ready to be clicked* in `docs/design-notes.md` for how that was chased
    // down and for the two weaker gates that looked right and were not.
    //
    // It is still not enough for anything that depends on a **JS module**: those are imported in
    // `OnAfterRenderAsync`, which happens later still, so a spec touching one needs its own gate — see
    // `Clipboard.WaitForReadyAsync`.
    private const string HandlersWired =
        """
        () => [...document.querySelectorAll('*')]
            .some(element => [...element.attributes].some(a => a.name.startsWith('_bl_')))
        """;

    internal async Task GoAsync(string route = "/")
    {
        await Page.RunAndWaitForWebSocketAsync(() => Page.GotoAsync(route));

        await Page.WaitForFunctionAsync("() => window.Blazor !== undefined");
        await Page.Locator("div.act-root").WaitForAsync();
        await Page.WaitForFunctionAsync(HandlersWired);
    }

    internal static Card Card(int number, string title, BoardColumn column, string workingDir = "/dev/act")
        => new()
        {
            Number = number,
            Title = title,
            InitialPrompt = $"Do the work for {title}",
            Column = column,
            AgentType = AgentType.ClaudeCode,
            WorkingDir = workingDir,
            Schedule = TaskSchedule.Manual,
        };
}
