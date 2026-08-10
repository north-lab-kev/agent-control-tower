using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Deliberately not a second board — an archived card is not work in progress, so it gets a list. Two things
// here are worth more than the list: *Clear archive* is the only irreversible action in the app, and it is
// hidden while a filter is up, because it would empty the whole archive rather than what the list is showing.
public class ArchiveViewTests : ComponentTest
{
    [Fact]
    public async Task An_empty_archive_says_so_and_offers_nothing()
    {
        await BoardWith(Live(1));

        var cut = Show();

        cut.Find(".empty").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll("div.row").Should().BeEmpty();
        cut.FindAll("div.purge button").Should().BeEmpty();
        cut.FindAll("div.cardfilter").Should().BeEmpty("nothing to filter");
    }

    [Fact]
    public async Task An_archived_card_becomes_a_row()
    {
        await BoardWith(Archived(1042, "Rename the widget"));

        var cut = Show();

        cut.Find("div.row .id").TextContent.Should().Be("#1042");
        cut.Find("div.row .ttl").TextContent.Should().Be("Rename the widget");
    }

    [Fact]
    public async Task The_board_own_cards_are_not_in_the_archive()
    {
        await BoardWith(Archived(1, "Gone"), Live(2));

        Show().FindAll("div.row").Should().ContainSingle();
    }

    // A link rather than a click handler: an archived card is a route like any other, so it gets the
    // middle-click, the keyboard and the hover target the browser already knows how to give it.
    [Fact]
    public async Task A_row_is_a_real_link_to_the_card()
    {
        var card = Archived(1042, "Rename the widget");

        await BoardWith(card);

        Show().Find("div.row a.what").GetAttribute("href").Should().Be($"/card/{card.Id}/edit");
    }

    [Fact]
    public async Task A_row_says_enough_to_recognise_the_card_without_opening_it()
    {
        var card = Archived(1042, "Rename the widget");

        await BoardWith(card);

        Show().Find("div.row .meta").TextContent.Should().Contain("/dev/act").And.Contain("Completed");
    }

    // Whether the user or the retention window put it here, which is the one thing a row cannot infer.
    [Fact]
    public async Task A_row_says_whether_retention_archived_it()
    {
        var swept = Archived(1, "Swept");
        swept.DeletedAt = null;
        swept.ArchivedAt = Now.AddDays(-8);

        await BoardWith(swept, Archived(2, "By hand"));

        var metas = Show().FindAll("div.row .meta").Select(meta => meta.TextContent).ToList();

        metas.Should().ContainSingle(meta => meta.Contains("utomatically"));
    }

    // The board hands its query over rather than making the user type it twice — it is the same question,
    // asked of the half of the store the board cannot show.
    [Fact]
    public async Task The_query_the_board_handed_over_is_already_in_the_box()
    {
        await BoardWith(Archived(1, "Rename the widget"), Archived(2, "Something else"));

        Navigation.NavigateTo("/archive?q=widget");

        var cut = Show();

        cut.Find("div.cardfilter input.box").GetAttribute("value").Should().Be("widget");
        cut.FindAll("div.row").Should().ContainSingle();
    }

    // Once: a cascading value changing must not throw away what the user has typed since.
    [Fact]
    public async Task What_the_user_typed_survives_a_re_render()
    {
        await BoardWith(Archived(1, "Rename the widget"), Archived(2, "Something else"));

        Navigation.NavigateTo("/archive?q=widget");

        var cut = Show();

        cut.Find("div.cardfilter input.box").Input("something");
        cut.Render();

        cut.Find("div.cardfilter input.box").GetAttribute("value").Should().Be("something");
        cut.Find("div.row .ttl").TextContent.Should().Be("Something else");
    }

    [Fact]
    public async Task A_query_that_matches_nothing_says_so_rather_than_reading_as_an_empty_archive()
    {
        await BoardWith(Archived(1, "Rename the widget"));

        var cut = Show();

        cut.Find("div.cardfilter input.box").Input("nothing like it");

        cut.Find(".empty").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll("div.row").Should().BeEmpty();
    }

    [Fact]
    public async Task Restoring_puts_the_card_back_on_the_board()
    {
        var card = Archived(1042, "Rename the widget");

        await BoardWith(card);

        Show().Find("div.row button").Click();

        Board.Card(card.Id)!.IsOnBoard.Should().BeTrue();
    }

    // The archived card stays archived: the copy is a live task, and opening it is what makes that obvious —
    // the archive itself would look unchanged.
    [Fact]
    public async Task Duplicating_leaves_the_archived_card_alone_and_opens_the_copy()
    {
        var card = Archived(1042, "Rename the widget");

        await BoardWith(card);

        var cut = Show();

        cut.FindAll("div.row button")[1].Click();

        cut.WaitForAssertion(() => Board.In(BoardColumn.Preparing).Should().ContainSingle());

        Board.Card(card.Id)!.IsOnBoard.Should().BeFalse();
        Route.Should().Be($"card/{Board.In(BoardColumn.Preparing)[0].Id}/edit");
    }

    // Also a dialog: this is the one irreversible action in the app, so it must not be quieter than the
    // reversible one next to it.
    [Fact]
    public async Task Emptying_the_archive_asks_first_and_names_the_count()
    {
        await BoardWith(Archived(1, "One"), Archived(2, "Two"));

        var cut = Show();

        cut.Find("div.purge button").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Board.Archived.Should().HaveCount(2, "nothing goes until the question is answered");
    }

    [Fact]
    public async Task Confirming_empties_it()
    {
        await BoardWith(Archived(1, "One"), Archived(2, "Two"));

        var cut = Show();

        cut.Find("div.purge button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, true);

        cut.WaitForAssertion(() => Board.Archived.Should().BeEmpty());
    }

    [Fact]
    public async Task Declining_leaves_it_alone()
    {
        await BoardWith(Archived(1, "One"));

        var cut = Show();

        cut.Find("div.purge button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, false);

        Board.Archived.Should().ContainSingle();
    }

    // Gone while a filter is up: *Clear archive* empties what the archive holds, and a filtered list is not
    // what it would empty.
    [Fact]
    public async Task There_is_no_emptying_while_a_filter_is_up()
    {
        await BoardWith(Archived(1, "Rename the widget"), Archived(2, "Something else"));

        var cut = Show();

        cut.FindAll("div.purge button").Should().ContainSingle();

        cut.Find("div.cardfilter input.box").Input("widget");

        cut.FindAll("div.purge button").Should().BeEmpty();
    }

    [Fact]
    public async Task The_list_follows_the_store()
    {
        var card = Archived(1042, "Rename the widget");

        await BoardWith(card);

        var cut = Show();

        cut.FindAll("div.row").Should().ContainSingle();

        await Board.RestoreAsync(Board.Card(card.Id)!);

        cut.WaitForAssertion(() => cut.FindAll("div.row").Should().BeEmpty());
    }

    [Fact]
    public async Task The_question_counts_what_it_would_delete()
    {
        await BoardWith(Archived(1, "One"), Archived(2, "Two"));

        Show().Instance.PurgeQuestion.Should().Contain("all 2 archived tasks");
    }

    // The archive spends most of its life small, so the singular is not the rare case — and it is the
    // one the old `archived task(s)` read worst for.
    [Fact]
    public async Task One_archived_task_is_asked_about_in_the_singular()
    {
        await BoardWith(Archived(1, "One"));

        Show().Instance.PurgeQuestion.Should().Contain("the archived task?").And.NotContain("1");
    }

    // A filter narrows the list but not what emptying would take, so the question must go on counting
    // the archive rather than the rows on screen.
    [Fact]
    public async Task The_question_counts_the_archive_and_not_the_filtered_rows()
    {
        await BoardWith(Archived(1, "Rename the widget"), Archived(2, "Something else"));

        var cut = Show();

        cut.Find("div.cardfilter input.box").Input("widget");

        cut.Instance.PurgeQuestion.Should().Contain("all 2 archived tasks");
    }

    private IRenderedComponent<ArchiveView> Show() => Render<ArchiveView>();

    private Card Archived(int number, string title)
    {
        var card = Live(number, title);

        card.Column = BoardColumn.Completed;
        card.DeletedAt = Now.AddDays(-1);

        return card;
    }

    private static Card Live(int number, string title = "Live") => new()
    {
        Number = number,
        Title = title,
        Column = BoardColumn.Ready,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
    };
}
