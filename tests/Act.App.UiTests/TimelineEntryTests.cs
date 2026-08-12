using System.Globalization;
using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// A card's history rail. Two decisions worth being sure of: how much of a date a row shows, and what
// a row says when the transition behind it carries no reason at all.
public class TimelineEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    public TimelineEntryTests() => CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");

    // Oldest first: a timeline that reads downwards is the one the eye follows, and the connecting
    // rail only makes sense in the direction the work went.
    [Fact]
    public void The_rail_reads_oldest_first_whatever_order_the_transitions_are_in()
    {
        var card = new Card();

        card.Transitions.Add(Transition(Now.AddHours(-1), TransitionReason.TurnEnded));
        card.Transitions.Add(Transition(Now.AddHours(-3), TransitionReason.Launched));
        card.Transitions.Add(Transition(Now.AddHours(-2), TransitionReason.ActivityObserved));

        TimelineEntry.For(card, Now)
            .Select(entry => entry.At)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_card_nothing_has_happened_to_has_an_empty_rail()
    {
        TimelineEntry.For(new Card(), Now).Should().BeEmpty();
    }

    // Today needs only a clock.
    [Fact]
    public void Something_that_happened_today_shows_only_the_time()
    {
        var entry = TimelineEntry.Of(Transition(Now.AddHours(-2), TransitionReason.Launched), Now);

        entry.Clock.Should().MatchRegex(@"^\d{2}:\d{2}:\d{2}$");
    }

    // Earlier this year needs no year.
    [Fact]
    public void Something_earlier_this_year_shows_a_date_without_the_year()
    {
        var entry = TimelineEntry.Of(Transition(Now.AddMonths(-3), TransitionReason.Launched), Now);

        entry.Clock.Should().NotMatchRegex(@"^\d{2}:\d{2}:\d{2}$");
        entry.Clock.Should().NotContain(Now.Year.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Something_from_a_previous_year_shows_the_year_too()
    {
        var then = Now.AddYears(-2);
        var entry = TimelineEntry.Of(Transition(then, TransitionReason.Launched), Now);

        entry.Clock.Should().Contain(then.ToLocalTime().Year.ToString(CultureInfo.InvariantCulture));
    }

    // The dot beside an entry is the colour the badge was when it happened.
    [Fact]
    public void The_dot_takes_the_colour_the_badge_had()
    {
        var moved = Transition(Now, TransitionReason.TurnFailed);
        moved.Badge = Badge.Error;

        TimelineEntry.Of(moved, Now).RailClass.Should().Be("b-err");
    }

    // A hand move has no badge, so the column it landed in supplies the colour instead.
    [Theory]
    [InlineData(BoardColumn.Ready, "b-ready")]
    [InlineData(BoardColumn.Completed, "b-done")]
    [InlineData(BoardColumn.Preparing, "b-plan")]
    public void Without_a_badge_the_column_supplies_the_colour(BoardColumn column, string expected)
    {
        var moved = Transition(Now, TransitionReason.MovedByHand);
        moved.Column = column;

        TimelineEntry.Of(moved, Now).RailClass.Should().Be(expected);
    }

    [Fact]
    public void A_row_says_what_the_reason_means_and_where_it_landed()
    {
        var moved = Transition(Now, TransitionReason.MovedByHand);
        moved.Column = BoardColumn.Ready;

        var entry = TimelineEntry.Of(moved, Now);

        entry.Text.Should().NotBeEmpty();
        entry.Column.Should().Be(BoardColumn.Ready);
    }

    // The verbatim half — an exit code, a CLI error — rides the wording rather than replacing it.
    [Fact]
    public void A_note_reaches_the_row()
    {
        var failed = Transition(Now, TransitionReason.LaunchFailed);
        failed.Note = "claude: not found";

        TimelineEntry.Of(failed, Now).Text.Should().Contain("claude: not found");
    }

    // Rows written before `TransitionReason` existed carry neither a reason nor a note, and a blank
    // line looks like a defect rather than like history.
    [Fact]
    public void A_row_with_no_reason_still_says_something()
    {
        var bare = new Transition { At = Now, Column = BoardColumn.Ready };

        var entry = TimelineEntry.Of(bare, Now);

        entry.Text.Should().NotBeEmpty();

        // Suppressed, or the row would read "Entered Ready → Ready".
        entry.Column.Should().BeNull();
    }

    [Fact]
    public void A_row_with_neither_reason_nor_column_is_left_blank_rather_than_invented()
    {
        var empty = new Transition { At = Now };

        TimelineEntry.Of(empty, Now).Text.Should().BeEmpty();
    }

    private static Transition Transition(DateTimeOffset at, TransitionReason reason) => new()
    {
        At = at,
        Column = BoardColumn.Executing,
        Reason = reason,
    };
}
