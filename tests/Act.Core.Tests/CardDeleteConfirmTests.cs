using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

// Which deletions stop to ask. The line is not "is this destructive" — every delete is — but "does this
// end something the archive cannot put back", and that is a live agent.
public class CardDeleteConfirmTests
{
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void A_card_with_a_live_agent_is_confirmed(BoardColumn column)
        => CardDeleteConfirm.IsRequired(column).Should().BeTrue();

    // Preparing and Ready have never launched; Completed has already finished. Deleting any of them
    // loses nothing but a row the archive holds onto, so asking would be ceremony on the gesture people
    // use to tidy the board.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public void A_card_with_no_live_agent_is_not(BoardColumn column)
        => CardDeleteConfirm.IsRequired(column).Should().BeFalse();

    // Every column is decided one way or the other, so a column added later cannot quietly default to
    // "no need to ask" — which is the direction that would cost somebody a running agent.
    [Fact]
    public void Every_column_is_accounted_for()
    {
        var columns = Enum.GetValues<BoardColumn>();

        columns.Count(CardDeleteConfirm.IsRequired).Should().Be(2);
        columns.Should().HaveCount(5, "a new column needs a decision here, not a default");
    }
}
