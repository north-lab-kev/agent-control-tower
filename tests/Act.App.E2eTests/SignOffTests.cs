using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S6 — the two hand-driven moves past the launch boundary, and the way back from the archive.
//
// Every one of these is a *write*, and the store underneath is real. bUnit proves the page calls the
// right service; this proves the row that comes back out of LiteDB afterwards is the one the user asked
// for.
public sealed class SignOffTests : BrowserTest
{
    private Guid CardId => App.Board.All.Single(card => card.Number == 1).Id;

    protected override void Arrange()
    {
        var waiting = Card(1, "Rename the widget", BoardColumn.YourTurn);
        waiting.Badge = Badge.ReadyForReview;

        App.Cards.Add(waiting);
    }

    // The review happens in front of the terminal, so that is where the sign-off lives — and signing off
    // ends the session, which would leave the view showing an empty pane for work that is done.
    [Fact]
    public async Task Signing_off_from_the_terminal_returns_to_the_board()
    {
        await GoAsync($"/card/{CardId}/terminal");

        await Act("Mark completed");

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
        await Assertions.Expect(Lane("Completed").Locator("div.strip")).ToHaveCountAsync(1);

        App.Board.Card(CardId)!.Column.Should().Be(BoardColumn.Completed);
    }

    // And its undo, in the same place: reading the transcript of a signed-off card is exactly when the
    // user finds the thing they still wanted to say.
    [Fact]
    public async Task Reopening_puts_it_back_and_stays_on_the_page()
    {
        await GoAsync($"/card/{CardId}/terminal");
        await Act("Mark completed");
        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();

        await GoAsync($"/card/{CardId}/terminal");
        await Act("Reopen");

        await Assertions.Expect(Page.Locator("div.session")).ToBeVisibleAsync();

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        App.Board.Card(CardId)!.Column.Should().Be(BoardColumn.YourTurn);
    }

    // Archiving is reversible and the archive page is the undo, so most cards need no confirmation — but
    // this one is in **Your turn**, where a session is parked waiting to be answered, and ending that is
    // what the archive cannot restore. `CardDeleteConfirm` owns the rule; this proves the dialog it asks
    // for actually renders and is answerable in a real browser, which is the half bUnit cannot show.
    [Fact]
    public async Task Archiving_a_card_with_a_live_session_asks_before_it_goes()
    {
        await GoAsync($"/card/{CardId}/edit");

        await Page.Locator("div.destructive button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.rz-dialog")).ToBeVisibleAsync();

        // The number says which card; the title must stay out of the sentence, because dropped into it it
        // reads as part of the prose rather than as a name.
        var asked = Page.Locator("div.rz-dialog");

        await Assertions.Expect(asked).ToContainTextAsync("Task #1 has an agent working on it");
        await Assertions.Expect(asked).Not.ToContainTextAsync("Rename the widget");

        App.Board.Card(CardId)!.IsOnBoard.Should().BeTrue("nothing goes until the question is answered");

        await ConfirmDeleteAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(0);

        App.Board.Card(CardId)!.IsOnBoard.Should().BeFalse();
    }

    // The other answer, in the browser: dismissing keeps the card *and* stays on its page, so a mis-click
    // costs nothing.
    [Fact]
    public async Task Declining_keeps_the_card_and_the_page()
    {
        await GoAsync($"/card/{CardId}/edit");

        await Page.Locator("div.destructive button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.rz-dialog")).ToBeVisibleAsync();

        await Page.Locator("div.rz-dialog button")
            .Filter(new LocatorFilterOptions { HasTextString = "Keep the task" })
            .ClickAsync();

        await Assertions.Expect(Page.Locator("div.rz-dialog")).Not.ToBeVisibleAsync();

        App.Board.Card(CardId)!.IsOnBoard.Should().BeTrue();
    }

    [Fact]
    public async Task An_archived_card_is_listed_in_the_archive()
    {
        await ArchiveAsync();

        await GoAsync("/archive");

        await Assertions.Expect(Page.Locator("div.archive div.row")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("div.archive div.row .ttl")).ToHaveTextAsync("Rename the widget");
    }

    [Fact]
    public async Task Restoring_puts_it_back_on_the_board()
    {
        await ArchiveAsync();

        await GoAsync("/archive");

        await Page.Locator("div.archive div.row button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.archive div.row")).ToHaveCountAsync(0);

        App.Board.Card(CardId)!.IsOnBoard.Should().BeTrue();

        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(1);
    }

    // The one irreversible action in the app, so it must not be quieter than the reversible one beside it.
    [Fact]
    public async Task Emptying_the_archive_asks_first()
    {
        await ArchiveAsync();

        await GoAsync("/archive");

        await Page.Locator("div.purge button").ClickAsync();

        await Assertions.Expect(Page.Locator("div.rz-dialog-content")).ToBeVisibleAsync();

        App.Board.Archived.Should().ContainSingle("nothing goes until the question is answered");
    }

    [Fact]
    public async Task Declining_leaves_the_archive_alone()
    {
        await ArchiveAsync();

        await GoAsync("/archive");

        await Page.Locator("div.purge button").ClickAsync();
        await Assertions.Expect(Page.Locator("div.rz-dialog-content")).ToBeVisibleAsync();

        await Page.Locator("div.rz-dialog-content button").Last.ClickAsync();

        await Assertions.Expect(Page.Locator("div.archive div.row")).ToHaveCountAsync(1);

        App.Board.Archived.Should().ContainSingle();
    }

    [Fact]
    public async Task Confirming_empties_it()
    {
        await ArchiveAsync();

        await GoAsync("/archive");

        await Page.Locator("div.purge button").ClickAsync();
        await Assertions.Expect(Page.Locator("div.rz-dialog-content")).ToBeVisibleAsync();

        await Page.Locator("div.rz-dialog-content button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.archive div.row")).ToHaveCountAsync(0);

        App.Board.Archived.Should().BeEmpty();
    }

    // The fixture card sits in Your turn, so every archive here goes through the delete confirmation. It
    // is part of the flow rather than a detail of one test, which is why it lives in the helper.
    private async Task ArchiveAsync()
    {
        await GoAsync($"/card/{CardId}/edit");

        await Page.Locator("div.destructive button").First.ClickAsync();

        await ConfirmDeleteAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    private Task ConfirmDeleteAsync()
        => Page.Locator("div.rz-dialog button")
            .Filter(new LocatorFilterOptions { HasTextString = "Delete and stop the agent" })
            .ClickAsync();

    private Task Act(string action)
        => Page.Locator("div.actions button")
            .Filter(new LocatorFilterOptions { HasTextString = action })
            .ClickAsync();

    private ILocator Lane(string name)
        => Page.Locator("div.col").Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".colname", new PageLocatorOptions { HasTextString = name }),
        });
}
