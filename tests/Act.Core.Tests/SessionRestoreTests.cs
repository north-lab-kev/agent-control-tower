using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class SessionRestoreTests
{
    // Executing is work that was running, and Your turn is work whose next step is said in the
    // terminal that went away — the answer to a prompt, or a send-back after review.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void A_bound_card_in_the_machine_region_is_resumable(BoardColumn column)
        => SessionRestore.IsResumable(CardIn(column)).Should().BeTrue();

    // Killed and errored cards live in Your turn and come back too — the process is gone, the
    // transcript is not, and the whole point is that a restart does not cost the terminal.
    [Theory]
    [InlineData(Badge.Killed)]
    [InlineData(Badge.Error)]
    public void The_badge_it_died_with_does_not_bar_it(Badge badge)
        => SessionRestore.IsResumable(CardIn(BoardColumn.YourTurn, badge)).Should().BeTrue();

    // Restoring resumes a session id; it never starts one. Ready is a launch the user has not made
    // yet, and Completed is done — it resumes on reopen, as an explicit act.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public void A_card_outside_the_machine_region_is_left_alone(BoardColumn column)
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

    private static Card CardIn(BoardColumn column, Badge? badge = null)
        => new()
        {
            Column = column,
            Badge = badge,
            SessionId = "abc",
        };
}
