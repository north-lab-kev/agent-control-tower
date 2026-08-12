using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class NotificationTriggerTests
{
    [Theory]
    [InlineData(Badge.NeedsPermission)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.Error)]
    [InlineData(Badge.Killed)]
    [InlineData(Badge.ReadyForReview)]
    public void Every_reason_a_card_wants_you_is_worth_a_ping(Badge badge)
        => NotificationTrigger.Wants(CardIn(BoardColumn.YourTurn, badge)).Should().BeTrue();

    // The pulse on the board and the ping on the desktop are the same claim, so the two must agree
    // on every badge — this is what pins them together as the set grows or shrinks.
    [Theory]
    [InlineData(Badge.Running)]
    [InlineData(Badge.Compacting)]
    [InlineData(null)]
    public void Work_in_flight_is_not(Badge? badge)
        => NotificationTrigger.Wants(CardIn(BoardColumn.YourTurn, badge)).Should().BeFalse();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.Completed)]
    public void No_other_column_ever_pings(BoardColumn column)
        => NotificationTrigger.Wants(CardIn(column, Badge.NeedsPermission)).Should().BeFalse();

    [Fact]
    public void A_deleted_card_does_not_ping()
    {
        var card = CardIn(BoardColumn.YourTurn, Badge.Error);
        card.DeletedAt = DateTimeOffset.UnixEpoch;

        NotificationTrigger.Wants(card).Should().BeFalse();
    }

    [Fact]
    public void An_archived_card_does_not_ping()
    {
        var card = CardIn(BoardColumn.YourTurn, Badge.Error);
        card.ArchivedAt = DateTimeOffset.UnixEpoch;

        NotificationTrigger.Wants(card).Should().BeFalse();
    }

    private static Card CardIn(BoardColumn column, Badge? badge)
        => new() { Column = column, Badge = badge };
}
