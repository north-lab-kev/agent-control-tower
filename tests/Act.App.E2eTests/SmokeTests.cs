using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S7 — the capstone. One task, from nothing to archived, in one narrative.
//
// It duplicates S2–S6 on purpose. What it adds is the *transitions between* them, which is where state
// left behind hides: a form that armed its unsaved guard and never disarmed it, a session the sign-off
// did not end, a strip that stayed in two lanes at once. None of the smaller specs can see any of that,
// because each of them starts from a board somebody else wrote.
//
// It is also the slowest thing here and the most likely to flake, so it is the first thing to cut if
// this suite ever starts costing more than it returns.
public sealed class SmokeTests : BrowserTest
{
    protected override void Arrange()
        => App.Claude.Script = AgentScript.Start()
            .Activity("Read")
            .Paints("Rename Widget to Gadget everywhere? [y/n] ")
            .AwaitsKeystroke()
            .RequestsPermission("write to src/Widget.cs")
            .AwaitsKeystroke()
            .EndsTurn()
            .AwaitsKeystroke();

    [Fact]
    public async Task A_task_goes_all_the_way_through()
    {
        await GoAsync();

        // 1. An empty board.
        await Assertions.Expect(Page.Locator("div.col")).ToHaveCountAsync(5);
        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(0);

        // 2. A task is written and saved.
        await Page.Locator("button.newtaskbtn").ClickAsync();
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();

        await Page.Locator("div.titlerow input").FillAsync("Rename the widget");
        await Page.Locator("textarea").FillAsync("Rename Widget to Gadget across the solution");
        await Page.Locator("div.dirrow input").FillAsync("/dev/act");
        await Page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(Lane("Preparing").Locator("div.strip")).ToHaveCountAsync(1);

        var card = App.Board.All.Should().ContainSingle().Subject;

        // 3. Launched from the board. Preparing is promoted on the way, so the strip crosses two lanes.
        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Lane("Preparing").Locator("div.strip")).ToHaveCountAsync(0);

        // 4. The terminal shows what the agent painted.
        await Page.Locator("div.col div.strip").ClickAsync();

        await Assertions.Expect(Page.Locator("div.session")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows"))
            .ToContainTextAsync("Rename Widget to Gadget everywhere?");

        // 5. Answered in the terminal, which unblocks the agent — it then asks for permission, and the
        //    card becomes the user's turn while the page just sits there.
        await Page.Locator("div.terminal .xterm-screen").ClickAsync();
        await Page.Keyboard.TypeAsync("y");

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("needs permission");

        // 6. Permission given the same way; the agent finishes its turn.
        await Page.Keyboard.TypeAsync("y");

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        // 7. Signed off, which ends the session and returns to the board.
        await Page.Locator("div.actions button")
            .Filter(new LocatorFilterOptions { HasTextString = "Mark completed" })
            .ClickAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
        await Assertions.Expect(Lane("Completed").Locator("div.strip")).ToHaveCountAsync(1);

        App.Registry.For(card.Id).Should().BeNull("signing off ends the session");

        // 8. Archived, and the board is empty again.
        await GoAsync($"/card/{card.Id}/edit");
        await Page.Locator("div.destructive button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(0);

        // 9. And it is in the archive, where the whole run is still readable.
        await GoAsync("/archive");

        await Assertions.Expect(Page.Locator("div.archive div.row")).ToHaveCountAsync(1);

        // 10. The history the card accumulated on the way, which is the only record that it all happened.
        await GoAsync($"/card/{card.Id}/timeline");

        await Assertions.Expect(Page.Locator("div.tl div.ev").First).ToBeVisibleAsync();

        var stored = App.Board.Card(card.Id)!;

        stored.Column.Should().Be(BoardColumn.Completed);
        stored.IsOnBoard.Should().BeFalse();
        stored.Transitions.Should().NotBeEmpty();
    }

    private ILocator Lane(string name)
        => Page.Locator("div.col").Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".colname", new PageLocatorOptions { HasTextString = name }),
        });
}
