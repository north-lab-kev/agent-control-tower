using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// `act-unsaved` guards the one exit Blazor cannot see: the window's own close button. Every move *inside*
// ACT arrives as a location change and is refused in C# — that half is covered by `TaskViewExitTests`.
// This half is `beforeunload`, which only a real browser has.
//
// The module is armed and disarmed from `OnAfterRenderAsync` as the form's dirty state changes, so what
// these check is that the arming tracks the form rather than being switched on once and left.
public sealed class UnsavedTests : BrowserTest
{
    protected override void Arrange()
        => App.Cards.Add(Card(1, "Rename the widget", BoardColumn.Ready));

    [Fact]
    public async Task A_clean_form_lets_the_window_close()
    {
        await GoToCardAsync();

        (await IsArmedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Typing_arms_the_guard()
    {
        await GoToCardAsync();

        await Page.Locator("div.titlerow input").FillAsync("Something else");
        await Page.Locator("textarea").ClickAsync();

        await Assertions.Expect(Page.Locator("div.titlerow input")).ToHaveValueAsync("Something else");

        await WaitUntilArmedAsync(true);
    }

    // The other direction, and the one that is easy to get wrong: typing the original value back makes the
    // form clean again, and a guard that only ever arms would then block a close for nothing.
    [Fact]
    public async Task Putting_the_text_back_disarms_it()
    {
        await GoToCardAsync();

        await Page.Locator("div.titlerow input").FillAsync("Something else");
        await Page.Locator("textarea").ClickAsync();
        await WaitUntilArmedAsync(true);

        await Page.Locator("div.titlerow input").FillAsync("Rename the widget");
        await Page.Locator("textarea").ClickAsync();

        await WaitUntilArmedAsync(false);
    }

    [Fact]
    public async Task Saving_disarms_it()
    {
        await GoToCardAsync();

        await Page.Locator("div.titlerow input").FillAsync("Renamed by the browser");
        await Page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();

        App.Board.In(BoardColumn.Ready).Single().Title.Should().Be("Renamed by the browser");
    }

    // What the guard is actually for. A `beforeunload` that is armed makes the browser ask before it
    // unloads, and Playwright surfaces that as a dialog — so this is the closest a test gets to the user
    // clicking the window's X.
    [Fact]
    public async Task An_armed_guard_asks_before_the_page_unloads()
    {
        await GoToCardAsync();

        await Page.Locator("div.titlerow input").FillAsync("Something else");
        await Page.Locator("textarea").ClickAsync();
        await WaitUntilArmedAsync(true);

        var asked = false;

        Page.Dialog += async (_, dialog) =>
        {
            asked = dialog.Type == "beforeunload";

            await dialog.AcceptAsync();
        };

        await Page.GotoAsync("about:blank");

        asked.Should().BeTrue("an armed guard has to stop the window closing on unsaved work");
    }

    // `arm(false)` is not the same as never arming: the module keeps the listener and only stops objecting.
    [Fact]
    public async Task A_clean_form_never_asks()
    {
        await GoToCardAsync();

        var asked = false;

        Page.Dialog += async (_, dialog) =>
        {
            asked = true;

            await dialog.AcceptAsync();
        };

        await Page.GotoAsync("about:blank");

        asked.Should().BeFalse();
    }

    // Read off the module's own effect rather than a flag it exposes: an armed `beforeunload` is one that
    // cancels the event, so dispatching one and reading `defaultPrevented` is the honest question.
    private Task<bool> IsArmedAsync()
        => Page.EvaluateAsync<bool>(
            """
            () => {
                const event = new Event('beforeunload', { cancelable: true });

                window.dispatchEvent(event);

                return event.defaultPrevented;
            }
            """);

    private async Task WaitUntilArmedAsync(bool armed)
        => await Page.WaitForFunctionAsync(
            """
            (armed) => {
                const event = new Event('beforeunload', { cancelable: true });

                window.dispatchEvent(event);

                return event.defaultPrevented === armed;
            }
            """,
            armed,
            new PageWaitForFunctionOptions { PollingInterval = 100 });

    private async Task GoToCardAsync()
    {
        var id = App.Board.In(BoardColumn.Ready).Single().Id;

        await GoAsync($"/card/{id}/edit");
        await Clipboard.WaitForReadyAsync(Page);
    }
}
