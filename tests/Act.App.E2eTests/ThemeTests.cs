using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// E4 — the only place in the build where CSS is more than a class name. bUnit reads `r-run` off an
// element and stops there; whether that token resolves to a colour, and whether the colour changes with
// the theme, is a question only a browser can answer.
//
// Deliberately computed styles rather than screenshots. Pixel snapshots are flaky across platforms and
// every legitimate CSS change costs a re-baseline — a cost a solo project should not take on.
public sealed class ThemeTests : BrowserTest
{
    protected override void Arrange()
    {
        App.Cards.Add(Card(1, "Running task", BoardColumn.Executing));
        App.Cards.Add(Card(2, "Waiting task", BoardColumn.Ready));

        App.Cards[0].Badge = Badge.Running;
    }

    // The rail is a custom property resolved per state — `--rail` set by `r-run`, `r-ready` and the rest.
    // A token that does not resolve leaves every strip the same colour, which no component test notices.
    [Fact]
    public async Task A_strip_rail_resolves_to_a_colour_per_state()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(2);

        var running = await RailAsync("div.strip.r-run");
        var ready = await RailAsync("div.strip.r-ready");

        // A resolved colour, not the `var(--act-run)` token it is declared as. If Chromium ever stopped
        // substituting custom properties at computed-value time this would still *differ* between the two
        // states — and would be comparing token text rather than anything a user sees.
        running.Should().StartWith("#");
        ready.Should().StartWith("#");
        running.Should().NotBe(ready, "a glance at the board is meant to separate them");
    }

    [Fact]
    public async Task The_control_room_repaints_for_the_theme()
    {
        await GoAsync();

        await ApplySchemeAsync(ColorScheme.Light);

        var light = await BackgroundAsync();

        await ApplySchemeAsync(ColorScheme.Dark);
        await WaitUntilChangedAsync("div.act-root", "backgroundColor", light);

        (await BackgroundAsync()).Should().NotBe(light);
    }

    [Fact]
    public async Task The_rail_colours_follow_the_theme_too()
    {
        await GoAsync();

        await ApplySchemeAsync(ColorScheme.Light);

        var light = await RailAsync("div.strip.r-run");

        light.Should().NotBeNullOrWhiteSpace();

        await ApplySchemeAsync(ColorScheme.Dark);
        await WaitUntilChangedAsync("div.strip.r-run", "--rail", light);

        (await RailAsync("div.strip.r-run"))
            .Should().NotBe(light, "a rail that stayed put would be unreadable in one of the two");
    }

    // Five detailed columns are meant to fit the stated floor without a horizontal scrollbar. It is the one
    // layout promise that is cheap to check and expensive to notice by hand.
    [Fact]
    public async Task Five_lanes_fit_the_narrowest_window_act_claims_to_support()
    {
        await GoAsync();

        await Page.SetViewportSizeAsync(960, 800);

        await Assertions.Expect(Page.Locator("div.col")).ToHaveCountAsync(5);

        var overflow = await Page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(0, "the board is not meant to scroll sideways");
    }

    // Emulating a colour scheme does not repaint synchronously — the style recalc lands a frame or two
    // later, and reading straight afterwards returns the value from *before* the switch.
    //
    // `WaitUntilChangedAsync` below handles that for a switch whose previous value is known, and for a
    // long time that was thought to be the whole problem. It is not: the **first** emulation of a test
    // has no previous value to wait against, and reading through it returns whatever scheme the browser
    // happened to start in. When that read came back dark, the light/dark comparison was seeded with the
    // dark value, the wait that followed was waiting for dark to stop being dark, and the test hung until
    // it timed out. Rare — one run in about a dozen full suites — and it looked like the assertion rather
    // than the setup.
    //
    // So every emulation goes through here instead. `matchMedia` flipping is the browser's own statement
    // that the new scheme is in force, and two frames after it is past the style recalc that follows.
    private async Task ApplySchemeAsync(ColorScheme scheme)
    {
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = scheme });

        await Page.WaitForFunctionAsync(
            "expected => window.matchMedia(`(prefers-color-scheme: ${expected})`).matches",
            scheme is ColorScheme.Dark ? "dark" : "light");

        await Page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private Task WaitUntilChangedAsync(string selector, string property, string previous)
        => Page.WaitForFunctionAsync(
            """
            ([selector, property, previous]) => {
                const element = document.querySelector(selector);
                if (!element) {
                    return false;
                }

                const style = getComputedStyle(element);
                const value = property.startsWith('--')
                    ? style.getPropertyValue(property).trim()
                    : style[property];

                return value !== previous;
            }
            """,
            new[] { selector, property, previous },
            new PageWaitForFunctionOptions { PollingInterval = 50 });

    private Task<string> RailAsync(string selector)
        => Page.Locator(selector).First.EvaluateAsync<string>(
            "element => getComputedStyle(element).getPropertyValue('--rail').trim()");

    private Task<string> BackgroundAsync()
        => Page.Locator("div.act-root").EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor");
}
