using Act.Core.Model;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// `act-escape` — Escape as "leave this page", listened for on the document because the key has to work
// wherever the focus happens to be. What makes it worth a browser is that it is a *negotiation*: three
// other things can claim the key first, and each claim is a real DOM condition the module reads.
public sealed class EscapeTests : BrowserTest
{
    protected override void Arrange()
        => App.Cards.Add(Card(1, "Rename the widget", BoardColumn.Ready));

    [Fact]
    public async Task Escape_leaves_the_page()
    {
        await GoToCardAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Escape_leaves_from_wherever_the_focus_happens_to_be()
    {
        await GoToCardAsync();

        await Page.Locator("textarea").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    // Anything nearer than the page keeps the key. For a dropdown the claim is not the `popupOpen()`
    // check — a Radzen dropdown panel is not a `.rz-popup` — it is `defaultPrevented`: the focused
    // dropdown's own keydown handler takes Escape and cancels it, and the module reads that.
    [Fact]
    public async Task An_open_dropdown_keeps_the_key()
    {
        await GoToCardAsync();

        var agent = Page.Locator("div.row", new PageLocatorOptions { HasTextString = "Agent" })
            .Locator("div.rz-dropdown").First;

        await agent.ClickAsync();

        await Assertions.Expect(agent).ToHaveAttributeAsync("aria-expanded", "true");

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(agent).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();
    }

    // The picker is part of the page rather than a popup of its own, so the page itself claims the key —
    // this is the C# half of the negotiation and it has to compose with the JS half.
    [Fact]
    public async Task An_open_folder_picker_is_closed_before_the_page_is_left()
    {
        await GoToCardAsync();

        await Page.Locator("div.dirrow button").ClickAsync();
        await Assertions.Expect(Page.Locator("div.picker")).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.picker")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    // The `Ignore` contract, and the only reason the parameter exists: Escape typed at an agent is a
    // message to the agent, not a request to close its terminal.
    [Fact]
    public async Task The_terminal_keeps_the_key_it_is_typed_into()
    {
        var id = App.Board.In(BoardColumn.Ready).Single().Id;

        await GoAsync($"/card/{id}/terminal");

        // The screen, not the helper textarea xterm hides off-view: clicking the screen is what a user
        // does, and it is what puts the focus where `Ignore=".xterm"` is looking.
        await Assertions.Expect(Page.Locator("div.terminal .xterm-screen")).ToBeVisibleAsync();

        await Page.Locator("div.terminal .xterm-screen").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.session")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Escape_outside_the_terminal_still_leaves_that_page()
    {
        var id = App.Board.In(BoardColumn.Ready).Single().Id;

        await GoAsync($"/card/{id}/terminal");

        await Page.Locator("aside.rail").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    // The module is imported in `OnAfterRenderAsync`, so pressing Escape the moment the markup appears
    // would race the binding. The drop zone answering is the cheapest proof the after-render pass has run.
    private async Task GoToCardAsync()
    {
        var id = App.Board.In(BoardColumn.Ready).Single().Id;

        await GoAsync($"/card/{id}/edit");
        await Clipboard.WaitForReadyAsync(Page);
    }
}
