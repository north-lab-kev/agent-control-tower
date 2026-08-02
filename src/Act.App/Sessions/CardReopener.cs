using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Sessions;

public sealed class CardReopener(BoardState board, IClock clock)
{
    public bool CanReopen(Card card) => CardReopen.CanReopen(card);

    public async Task<bool> ReopenAsync(Card card, CancellationToken cancellationToken = default)
    {
        if (!CanReopen(card))
            return false;

        card.Column = BoardColumn.YourTurn;
        card.Badge = Badge.ReadyForReview;
        card.CompletedAt = null;
        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = BoardColumn.YourTurn,
            Badge = Badge.ReadyForReview,
            Reason = TransitionReason.Reopened,
        });

        await board.UpdateAsync(card, cancellationToken);

        return true;
    }
}
