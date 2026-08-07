using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class TaskEditingTests
{
    // The two human-controlled columns are the ones before any session exists, so nothing on the
    // card has been acted on and all of it is still a proposal.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    public void A_card_that_has_never_launched_is_fully_editable(BoardColumn column)
        => TaskEditing.CanEditLaunchInputs(CardIn(column)).Should().BeTrue();

    // Everything past the launch boundary, Completed included: a signed-off card describes work
    // that was done, which is the strongest case of all for not rewriting what was asked.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void A_launched_card_freezes_what_defined_the_run(BoardColumn column)
        => TaskEditing.CanEditLaunchInputs(CardIn(column)).Should().BeFalse();

    // The column decides, not the session: a card whose pty died — to a kill, to a restart — still
    // ran, and the prompt it ran with is still the prompt it ran with.
    [Fact]
    public void Losing_the_session_does_not_reopen_the_fields()
    {
        var card = CardIn(BoardColumn.YourTurn);
        card.SessionId = null;
        card.Badge = Badge.Killed;

        TaskEditing.CanEditLaunchInputs(card).Should().BeFalse();
    }

    // Off the board, in the column that is otherwise the most editable of all: an archived card is a
    // record, so being openable is the whole of what it offers.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Completed)]
    public void An_archived_card_is_read_only(BoardColumn column)
    {
        var card = CardIn(column);
        card.DeletedAt = DateTimeOffset.UnixEpoch;

        TaskEditing.CanEdit(card).Should().BeFalse();
        TaskEditing.CanEditLaunchInputs(card).Should().BeFalse();
    }

    // Auto-archived is the same answer: which of the two took it off the board is a fact for the
    // archive row, not a difference in what the page allows.
    [Fact]
    public void The_retention_window_freezes_a_card_exactly_as_a_delete_does()
    {
        var card = CardIn(BoardColumn.Preparing);
        card.ArchivedAt = DateTimeOffset.UnixEpoch;

        TaskEditing.CanEdit(card).Should().BeFalse();
        TaskEditing.CanEditLaunchInputs(card).Should().BeFalse();
    }

    [Fact]
    public void A_card_on_the_board_is_editable()
        => TaskEditing.CanEdit(CardIn(BoardColumn.Executing)).Should().BeTrue();

    [Fact]
    public void Every_column_is_accounted_for()
        => Enum.GetValues<BoardColumn>().Should().HaveCount(5);

    private static Card CardIn(BoardColumn column) => new()
    {
        Title = "A task",
        Column = column,
        InitialPrompt = "do the thing",
    };
}
