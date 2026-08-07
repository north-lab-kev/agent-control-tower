using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class SessionRestoreTests
{
    // Executing is work that was running, and Your turn is work whose next step is said in the
    // terminal that went away — the answer to a prompt, or a send-back after review. Completed is
    // there for the history: the session is the record of what was done.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void A_bound_card_that_has_been_launched_is_resumable(BoardColumn column)
        => SessionRestore.IsResumable(CardIn(column)).Should().BeTrue();

    // Killed and errored cards live in Your turn and come back too — the process is gone, the
    // transcript is not, and the whole point is that a restart does not cost the terminal.
    [Theory]
    [InlineData(Badge.Killed)]
    [InlineData(Badge.Error)]
    public void The_badge_it_died_with_does_not_bar_it(Badge badge)
        => SessionRestore.IsResumable(CardIn(BoardColumn.YourTurn, badge)).Should().BeTrue();

    // Restoring resumes a session id; it never starts one. Ready is a launch the user has not made
    // yet, and Preparing is a card that has never been one.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    public void A_card_that_was_never_launched_is_left_alone(BoardColumn column)
        => SessionRestore.IsResumable(CardIn(column)).Should().BeFalse();

    [Fact]
    public void A_card_with_no_binding_has_nothing_to_resume()
    {
        var card = CardIn(BoardColumn.Executing);

        card.SessionId = null;

        SessionRestore.IsResumable(card).Should().BeFalse();
    }

    [Fact]
    public void An_archived_card_stays_off()
    {
        var card = CardIn(BoardColumn.Executing);

        card.DeletedAt = DateTimeOffset.UnixEpoch;

        SessionRestore.IsResumable(card).Should().BeFalse();
    }

    // The other way off the board, and it has to mean the same thing: opening a card the retention
    // window retired is reading a record, so it must not spawn a process either.
    [Fact]
    public void A_card_the_retention_window_took_stays_off_too()
    {
        var card = CardIn(BoardColumn.Completed);

        card.ArchivedAt = DateTimeOffset.UnixEpoch;

        SessionRestore.IsResumable(card).Should().BeFalse();
        SessionRestore.RestoresUnattended(card).Should().BeFalse();
    }

    // Startup restores the machine region only.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void Live_work_comes_back_by_itself(BoardColumn column)
        => SessionRestore.RestoresUnattended(CardIn(column)).Should().BeTrue();

    // A signed-off card waits to be opened: a pty per completed task at every start would be a fleet
    // of processes for work that is over.
    [Fact]
    public void A_completed_card_is_not_restored_unattended()
        => SessionRestore.RestoresUnattended(CardIn(BoardColumn.Completed)).Should().BeFalse();

    private static Card CardIn(BoardColumn column, Badge? badge = null)
        => new()
        {
            Column = column,
            Badge = badge,
            SessionId = "abc",
        };
}
