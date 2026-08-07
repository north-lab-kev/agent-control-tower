using Act.App.Cards;
using Act.App.Sessions;
using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

public class CardReopenerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reopening_hands_the_card_back_for_review()
    {
        var card = Completed();
        var reopener = await ReopenerOf(card);

        (await reopener.ReopenAsync(card)).Should().BeTrue();

        card.Column.Should().Be(BoardColumn.YourTurn);
        card.Badge.Should().Be(Badge.ReadyForReview);
    }

    // The retention sweep dates a card from its sign-off, so a card that is no longer signed off must
    // carry no sign-off stamp — otherwise reopening it would leave it archivable from Your turn.
    [Fact]
    public async Task Reopening_un_stamps_the_sign_off()
    {
        var card = Completed();
        var reopener = await ReopenerOf(card);

        await reopener.ReopenAsync(card);

        card.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Reopening_is_on_the_timeline()
    {
        var card = Completed();
        var reopener = await ReopenerOf(card);

        await reopener.ReopenAsync(card);

        card.Transitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                At = Now,
                Column = BoardColumn.YourTurn,
                Badge = Badge.ReadyForReview,
                Reason = TransitionReason.Reopened,
            });
    }

    [Fact]
    public async Task A_card_nobody_signed_off_is_left_where_it_is()
    {
        var card = Completed();
        card.Column = BoardColumn.Executing;

        var reopener = await ReopenerOf(card);

        (await reopener.ReopenAsync(card)).Should().BeFalse();

        card.Column.Should().Be(BoardColumn.Executing);
        card.Transitions.Should().BeEmpty();
    }

    private static async Task<CardReopener> ReopenerOf(params Card[] cards)
    {
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), new FrozenClock(Now));

        await board.LoadAsync();

        return new CardReopener(board, new FrozenClock(Now), NullLogger<CardReopener>.Instance);
    }

    private static Card Completed() => new()
    {
        Title = "Signed off",
        Column = BoardColumn.Completed,
        CompletedAt = Now.AddHours(-3),
        SessionId = "abc",
    };
}
