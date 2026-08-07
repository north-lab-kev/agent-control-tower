using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// `TimelineEntry` decides what a row says and `TimelineEntryTests` owns that. What is left is the rail: one
// row per stored transition, coloured by the badge that reported it, following the card while the page is
// open — and the card resolved per navigation rather than once, because the tabs move between a card's faces
// without leaving the component and the id is what changes.
public class TimelineViewTests : ComponentTest
{
    [Fact]
    public void An_id_that_is_not_there_says_so()
    {
        var cut = Render<TimelineView>(p => p.Add(c => c.CardId, Guid.NewGuid()));

        cut.Find("div.timeline.missing").TextContent.Should().Contain("no longer exists");
        cut.FindAll("div.tl").Should().BeEmpty();
    }

    [Fact]
    public async Task A_card_that_has_not_moved_yet_says_so_rather_than_drawing_an_empty_rail()
    {
        var cut = await Open(Card(BoardColumn.Preparing));

        cut.Find(".empty").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll("div.tl").Should().BeEmpty();
    }

    [Fact]
    public async Task Every_transition_becomes_a_row()
    {
        var card = Card(BoardColumn.YourTurn);
        card.Transitions =
        [
            Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30)),
            Moved(TransitionReason.TurnEnded, BoardColumn.YourTurn, Now.AddMinutes(-5)),
        ];

        var cut = await Open(card);

        cut.FindAll("div.tl div.ev").Should().HaveCount(2);
    }

    // A dot per event coloured by the badge that reported it, which is what makes a glance down the rail
    // read as a story rather than as a list.
    [Fact]
    public async Task A_row_takes_its_colour_from_what_reported_it()
    {
        var card = Card(BoardColumn.YourTurn);
        card.Transitions =
        [
            Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30), Badge.Running),
            Moved(TransitionReason.TurnFailed, BoardColumn.YourTurn, Now.AddMinutes(-5), Badge.Error),
        ];

        var cut = await Open(card);

        var rows = cut.FindAll("div.tl div.ev");

        rows[0].ClassName.Should().NotBe(rows[1].ClassName);
    }

    [Fact]
    public async Task A_row_names_the_column_the_card_moved_into()
    {
        var card = Card(BoardColumn.Executing);
        card.Transitions = [Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30))];

        var cut = await Open(card);

        cut.Find("div.ev .col").TextContent.Should().Contain("Executing");
    }

    // The mono time is held to the right and carries the full instant as its tooltip, because the clock
    // alone cannot say which day.
    [Fact]
    public async Task A_row_states_the_time_and_carries_the_date_behind_it()
    {
        var card = Card(BoardColumn.Executing);
        card.Transitions = [Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30))];

        var cut = await Open(card);

        var time = cut.Find("div.ev .tm");

        time.TextContent.Should().NotBeNullOrWhiteSpace();
        time.GetAttribute("title").Should().NotBeNullOrWhiteSpace().And.NotBe(time.TextContent);
    }

    // Subscribed for the same reason the session view is: the card moves while the user is looking at it.
    [Fact]
    public async Task The_rail_grows_as_the_card_moves_underneath_it()
    {
        var card = Card(BoardColumn.Executing);
        card.Transitions = [Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30))];

        var cut = await Open(card);

        cut.FindAll("div.ev").Should().ContainSingle();

        var stored = Board.Card(card.Id)!;
        stored.Transitions.Add(Moved(TransitionReason.TurnEnded, BoardColumn.YourTurn, Now));

        await Board.UpdateAsync(stored);

        cut.WaitForAssertion(() => cut.FindAll("div.ev").Should().HaveCount(2));
    }

    [Fact]
    public async Task Leaving_goes_back_where_the_card_lives()
    {
        var cut = await Open(Card(BoardColumn.Ready));

        cut.Find("header.bar button").Click();

        Route.Should().BeEmpty();
    }

    [Fact]
    public async Task An_archived_card_goes_back_to_the_archive()
    {
        var card = Card(BoardColumn.Completed);
        card.DeletedAt = Now.AddDays(-1);

        var cut = await Open(card);

        cut.Find("header.bar button").Click();

        Route.Should().Be("archive");
    }

    // The rail is cached against the card's transition count — a chatty board raises `Changed` once
    // a second for cards that are not this one — so the one thing the cache must prove is that a row
    // added to *this* card while the page is open still appears.
    [Fact]
    public async Task A_transition_added_while_the_page_is_open_becomes_a_row()
    {
        var card = Card(BoardColumn.Executing);
        card.Transitions = [Moved(TransitionReason.Launched, BoardColumn.Executing, Now.AddMinutes(-30))];

        var cut = await Open(card);

        cut.FindAll("div.tl div.ev").Should().ContainSingle();

        var moved = Board.Card(card.Id)!;

        moved.Transitions.Add(Moved(TransitionReason.TurnEnded, BoardColumn.YourTurn, Now.AddMinutes(-1)));

        await Board.UpdateAsync(moved);

        cut.WaitForAssertion(() => cut.FindAll("div.tl div.ev").Should().HaveCount(2));
    }

    private async Task<IRenderedComponent<TimelineView>> Open(Card card)
    {
        await BoardWith(card);

        Navigation.NavigateTo($"/card/{card.Id}/timeline");

        return Render<TimelineView>(p => p.Add(c => c.CardId, card.Id));
    }

    // The badge is what colours the row — the dot beside an entry is the colour the badge was when it
    // happened — so it is the thing worth naming here rather than the reason.
    private static Transition Moved(
        TransitionReason reason,
        BoardColumn to,
        DateTimeOffset at,
        Badge? badge = null)
        => new() { Reason = reason, Column = to, At = at, Badge = badge };

    private static Card Card(BoardColumn column) => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
    };
}
