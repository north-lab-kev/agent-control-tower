using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CardCompletionTests
{
    [Fact]
    public void A_card_handed_back_to_the_user_can_be_signed_off()
        => CardCompletion.CanComplete(CardIn(BoardColumn.YourTurn)).Should().BeTrue();

    // The column is the gate, not the badge. A card the user is looking at because it crashed or was
    // killed is as sign-off-able as one that finished cleanly — otherwise a failed session would have
    // no way to Completed but a round trip through the terminal.
    [Theory]
    [InlineData(Badge.ReadyForReview)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.NeedsPermission)]
    [InlineData(Badge.Error)]
    [InlineData(Badge.Killed)]
    public void The_reason_it_wants_attention_does_not_bar_the_sign_off(Badge badge)
    {
        var card = CardIn(BoardColumn.YourTurn);
        card.Badge = badge;

        CardCompletion.CanComplete(card).Should().BeTrue();
    }

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    public void A_card_that_has_not_been_handed_back_cannot_be(BoardColumn column)
        => CardCompletion.CanComplete(CardIn(column)).Should().BeFalse();

    [Fact]
    public void A_completed_card_is_not_completed_again()
        => CardCompletion.CanComplete(CardIn(BoardColumn.Completed)).Should().BeFalse();

    [Fact]
    public void An_archived_card_is_left_alone()
    {
        var card = CardIn(BoardColumn.YourTurn);
        card.DeletedAt = DateTimeOffset.UnixEpoch;

        CardCompletion.CanComplete(card).Should().BeFalse();
    }

    [Fact]
    public void The_sign_off_is_the_drop_onto_completed()
        => CardCompletion.CanCompleteInto(CardIn(BoardColumn.YourTurn), BoardColumn.Completed)
            .Should().BeTrue();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void No_other_column_signs_a_card_off(BoardColumn target)
        => CardCompletion.CanCompleteInto(CardIn(BoardColumn.YourTurn), target).Should().BeFalse();

    // The two halves of the human-controlled model must not overlap: the drop onto Completed stamps
    // the card and ends its session, so it must never be reachable as a plain move.
    [Fact]
    public void Completing_is_not_a_manual_move()
        => ManualMove.IsAllowed(BoardColumn.YourTurn, BoardColumn.Completed).Should().BeFalse();

    private static Card CardIn(BoardColumn column) => new() { Column = column };
}
