using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The board's drop-target logic, which until it left `BoardView` could not be exercised without
// rendering a component. Two promises run through all of it: a lane never lights up green for a
// drop it would then refuse, and the insertion marker never promises a position the drop does not
// deliver — both because the highlight, the marker and the drop all ask the same rules.
public class BoardDragTests
{
    [Fact]
    public void Nothing_is_lifted_to_begin_with()
    {
        var drag = new BoardDrag();

        drag.IsActive.Should().BeFalse();
        drag.OnStrip(Card(BoardColumn.Ready)).Should().BeNull();
        drag.IntoLane(BoardColumn.Ready).Should().BeNull();
    }

    [Theory]
    [InlineData(BoardColumn.Preparing, BoardColumn.Ready, true)]
    [InlineData(BoardColumn.Ready, BoardColumn.Preparing, true)]
    [InlineData(BoardColumn.YourTurn, BoardColumn.Completed, true)]
    [InlineData(BoardColumn.Completed, BoardColumn.YourTurn, true)]
    [InlineData(BoardColumn.Preparing, BoardColumn.Executing, false)]
    [InlineData(BoardColumn.Ready, BoardColumn.Completed, false)]
    [InlineData(BoardColumn.Executing, BoardColumn.YourTurn, false)]
    [InlineData(BoardColumn.YourTurn, BoardColumn.Ready, false)]
    public void A_lane_accepts_exactly_what_a_drop_would_take(BoardColumn from, BoardColumn to, bool accepted)
    {
        var card = Card(from);

        BoardDrag.Accepts(card, to).Should().Be(accepted);

        var drag = new BoardDrag();
        drag.Start(card);

        // The highlight and the decision are the same answer, which is the point of sharing a rule.
        (drag.IntoLane(to) is not null).Should().Be(accepted);
        drag.ColumnClass(to).Should().Be(accepted ? "col drop-ok" : "col drop-no");
    }

    // Sign-off and its undo are not moves — they stamp and un-stamp things a plain column change
    // does not — so the drop names them separately for the view to route.
    [Fact]
    public void Dropping_into_Completed_is_a_sign_off_rather_than_a_move()
    {
        var drag = new BoardDrag();
        drag.Start(Card(BoardColumn.YourTurn));

        drag.IntoLane(BoardColumn.Completed)!.Action.Should().Be(DropAction.Complete);
    }

    [Fact]
    public void Dragging_a_completed_card_back_is_a_reopen()
    {
        var drag = new BoardDrag();
        drag.Start(Card(BoardColumn.Completed));

        drag.IntoLane(BoardColumn.YourTurn)!.Action.Should().Be(DropAction.Reopen);
    }

    [Fact]
    public void An_ordinary_column_change_is_a_move()
    {
        var drag = new BoardDrag();
        drag.Start(Card(BoardColumn.Preparing));

        var drop = drag.IntoLane(BoardColumn.Ready)!;

        drop.Action.Should().Be(DropAction.Move);
        drop.Column.Should().Be(BoardColumn.Ready);
    }

    // The same gesture, told apart by where it lands.
    [Fact]
    public void Dropping_on_a_column_mate_reorders()
    {
        var moved = Card(BoardColumn.Ready, order: 1);
        var target = Card(BoardColumn.Ready, order: 2);

        var drag = new BoardDrag();
        drag.Start(moved);

        var drop = drag.OnStrip(target)!;

        drop.Action.Should().Be(DropAction.Reorder);
        drop.Target.Should().BeSameAs(target);
    }

    // A card crossing columns lands last, not wherever the cursor happened to be — so the strip it
    // was dropped on is discarded and the lane decides.
    [Fact]
    public void Dropping_on_a_strip_in_another_column_is_that_columns_drop()
    {
        var moved = Card(BoardColumn.Preparing);
        var target = Card(BoardColumn.Ready);

        var drag = new BoardDrag();
        drag.Start(moved);

        var drop = drag.OnStrip(target)!;

        drop.Action.Should().Be(DropAction.Move);
        drop.Target.Should().BeNull();
    }

    [Fact]
    public void Dropping_a_card_on_itself_does_nothing()
    {
        var card = Card(BoardColumn.Ready);

        var drag = new BoardDrag();
        drag.Start(card);

        drag.OnStrip(card).Should().BeNull();
    }

    // Dragged down, the card lands after the one under the cursor; dragged up, before it.
    [Fact]
    public void The_marker_sits_on_the_edge_the_drop_will_use()
    {
        var first = Card(BoardColumn.Ready, order: 1);
        var last = Card(BoardColumn.Ready, order: 2);
        var lane = new[] { first, last };

        var down = new BoardDrag();
        down.Start(first);
        down.OverStrip(last);

        down.EdgeOn(last, lane).Should().Be("drop-after");

        var up = new BoardDrag();
        up.Start(last);
        up.OverStrip(first);

        up.EdgeOn(first, lane).Should().Be("drop-before");
    }

    [Fact]
    public void The_marker_is_drawn_only_under_the_strip_being_hovered()
    {
        var first = Card(BoardColumn.Ready, order: 1);
        var last = Card(BoardColumn.Ready, order: 2);
        var lane = new[] { first, last };

        var drag = new BoardDrag();
        drag.Start(first);
        drag.OverStrip(last);

        drag.EdgeOn(first, lane).Should().BeNull();
    }

    // Hovering the lane between strips clears it, so it never outlives the card it was drawn under.
    [Fact]
    public void Leaving_the_strips_clears_the_marker()
    {
        var first = Card(BoardColumn.Ready, order: 1);
        var last = Card(BoardColumn.Ready, order: 2);
        var lane = new[] { first, last };

        var drag = new BoardDrag();
        drag.Start(first);
        drag.OverStrip(last);
        drag.OverLane();

        drag.EdgeOn(last, lane).Should().BeNull();
    }

    // A strip cannot be its own drop target, so hovering the one being dragged arms nothing.
    [Fact]
    public void Hovering_the_lifted_strip_arms_nothing()
    {
        var card = Card(BoardColumn.Ready);

        var drag = new BoardDrag();
        drag.Start(card);
        drag.OverStrip(card);

        drag.Over.Should().BeNull();
    }

    // The marker belongs to a reorder, and a card from another column is not reordering.
    [Fact]
    public void No_marker_is_drawn_for_a_card_from_another_column()
    {
        var moved = Card(BoardColumn.Preparing);
        var target = Card(BoardColumn.Ready, order: 1);

        var drag = new BoardDrag();
        drag.Start(moved);
        drag.OverStrip(target);

        drag.EdgeOn(target, [target]).Should().BeNull();
    }

    [Fact]
    public void Ending_the_gesture_forgets_everything()
    {
        var card = Card(BoardColumn.Ready, order: 1);
        var other = Card(BoardColumn.Ready, order: 2);

        var drag = new BoardDrag();
        drag.Start(card);
        drag.OverStrip(other);
        drag.End();

        drag.IsActive.Should().BeFalse();
        drag.Over.Should().BeNull();
        drag.IsLifted(card).Should().BeFalse();
        drag.ColumnClass(BoardColumn.Preparing).Should().Be("col");
    }

    // The lane a card came from is never a drop target of its own.
    [Fact]
    public void The_cards_own_column_is_left_neutral()
    {
        var drag = new BoardDrag();
        drag.Start(Card(BoardColumn.Ready));

        drag.ColumnClass(BoardColumn.Ready).Should().Be("col");
    }

    private static Card Card(BoardColumn column, int order = 0) => new()
    {
        Number = 1000 + order,
        Title = $"Card in {column}",
        Column = column,
        Order = order,
    };
}
