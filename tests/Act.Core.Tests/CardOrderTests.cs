using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CardOrderTests
{
    [Fact]
    public void Cards_stored_before_ordering_existed_sort_by_number()
    {
        Card[] column = [At(1041), At(1039), At(1040)];

        CardOrder.Sort(column).Select(card => card.Number).Should().Equal(1039, 1040, 1041);
    }

    [Fact]
    public void An_arriving_card_lands_past_everything_in_the_column()
    {
        var arriving = At(1042);

        CardOrder.Last([At(1039, 1), At(1040, 4), At(1041, 2)], arriving).Should().Be(5);
    }

    [Fact]
    public void An_empty_column_takes_the_first_position()
    {
        CardOrder.Last([], At(1039)).Should().Be(1);
    }

    // The card is already sitting in the column when it is re-stamped, and its own position must not
    // be what it is asked to beat — that would push it one place further every save.
    [Fact]
    public void A_card_already_in_the_column_does_not_count_itself()
    {
        var arriving = At(1041, 7);

        CardOrder.Last([At(1039, 1), arriving], arriving).Should().Be(2);
    }

    [Fact]
    public void Dragged_down_a_card_lands_after_the_one_it_was_dropped_on()
    {
        Card[] column = [At(1039, 1), At(1040, 2), At(1041, 3), At(1042, 4)];

        CardOrder.Move(column, column[0], column[2]);

        CardOrder.Sort(column).Select(card => card.Number).Should().Equal(1040, 1041, 1039, 1042);
    }

    [Fact]
    public void Dragged_up_a_card_lands_before_the_one_it_was_dropped_on()
    {
        Card[] column = [At(1039, 1), At(1040, 2), At(1041, 3), At(1042, 4)];

        CardOrder.Move(column, column[3], column[1]);

        CardOrder.Sort(column).Select(card => card.Number).Should().Equal(1039, 1042, 1040, 1041);
    }

    // The marker the board draws and the move the drop performs come from the same reading, so one
    // cannot promise a position the other does not deliver.
    [Fact]
    public void The_marker_agrees_with_the_move()
    {
        Card[] column = [At(1039, 1), At(1040, 2), At(1041, 3)];

        CardOrder.LandsAfter(column, column[0], column[2]).Should().BeTrue();
        CardOrder.LandsAfter(column, column[2], column[0]).Should().BeFalse();
    }

    [Fact]
    public void Only_the_cards_that_moved_are_handed_back()
    {
        Card[] column = [At(1039, 1), At(1040, 2), At(1041, 3), At(1042, 4)];

        CardOrder.Move(column, column[0], column[1])
            .Select(card => card.Number)
            .Should().BeEquivalentTo([1039, 1040]);
    }

    // A reorder resequences from one, so a column of cards that never had a position gets one
    // without the drag being read as "everything moved".
    [Fact]
    public void A_column_that_was_never_ordered_is_numbered_by_the_first_drag()
    {
        Card[] column = [At(1039), At(1040), At(1041)];

        CardOrder.Move(column, column[2], column[0]);

        CardOrder.Sort(column).Select(card => card.Order).Should().Equal(1, 2, 3);
        CardOrder.Sort(column).Select(card => card.Number).Should().Equal(1041, 1039, 1040);
    }

    [Fact]
    public void Dropping_a_card_on_itself_changes_nothing()
    {
        Card[] column = [At(1039, 1), At(1040, 2)];

        CardOrder.Move(column, column[0], column[0]).Should().BeEmpty();
    }

    [Fact]
    public void A_card_that_is_not_in_the_column_changes_nothing()
    {
        Card[] column = [At(1039, 1), At(1040, 2)];

        CardOrder.Move(column, At(1041, 1), column[0]).Should().BeEmpty();
    }

    private static Card At(int number, int order = 0) => new()
    {
        Number = number,
        Title = $"Task {number}",
        Column = BoardColumn.Ready,
        Order = order,
    };
}
