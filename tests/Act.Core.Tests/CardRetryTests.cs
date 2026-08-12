using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CardRetryTests
{
    [Fact]
    public void A_failed_card_whose_process_is_gone_can_be_retried()
        => CardRetry.CanRetry(Failed(), sessionLive: false).Should().BeTrue();

    // The rule that keeps ACT out of the terminal: a CLI still running is one the user can type
    // into, so retrying it would mean killing a session that is perfectly usable.
    [Fact]
    public void A_failed_card_whose_session_is_still_live_is_not_offered_a_retry()
        => CardRetry.CanRetry(Failed(), sessionLive: true).Should().BeFalse();

    // Retry continues a run. A card that failed before it ever bound a session has nothing to
    // continue — duplicating the task is how you start one over.
    [Fact]
    public void A_card_with_no_session_cannot_be_retried()
    {
        var card = Failed();
        card.SessionId = null;

        CardRetry.CanRetry(card, sessionLive: false).Should().BeFalse();
    }

    // Only `error`. A killed card was stopped on purpose and a reviewable one did not fail, so
    // neither is something to re-run behind the user's back.
    [Theory]
    [InlineData(Badge.Killed)]
    [InlineData(Badge.ReadyForReview)]
    [InlineData(Badge.NeedsPermission)]
    [InlineData(Badge.NeedsAnswer)]
    public void Only_an_error_is_retriable(Badge badge)
    {
        var card = Failed();
        card.Badge = badge;

        CardRetry.CanRetry(card, sessionLive: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.Completed)]
    public void Only_a_card_handed_back_to_the_user_is_retriable(BoardColumn column)
    {
        var card = Failed();
        card.Column = column;

        CardRetry.CanRetry(card, sessionLive: false).Should().BeFalse();
    }

    [Fact]
    public void An_archived_card_is_not_retriable()
    {
        var card = Failed();
        card.DeletedAt = DateTimeOffset.UtcNow;

        CardRetry.CanRetry(card, sessionLive: false).Should().BeFalse();
    }

    private static Card Failed() => new()
    {
        Title = "A task",
        Column = BoardColumn.YourTurn,
        Badge = Badge.Error,
        SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11",
    };
}
