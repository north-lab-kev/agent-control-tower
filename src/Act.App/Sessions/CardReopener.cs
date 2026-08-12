using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

public sealed class CardReopener(BoardState board, IClock clock, ILogger<CardReopener> log)
{
    public bool CanReopen(Card card) => CardReopen.CanReopen(card);

    public async Task<bool> ReopenAsync(Card card, CancellationToken cancellationToken = default)
    {
        using var scope = log.BeginTaskScope(card.Number, card.SessionId);

        if (!CanReopen(card))
        {
            log.LogInformation("Not reopened: a card in {Column} cannot be reopened.", card.Column);

            return false;
        }

        log.LogInformation("Reopened by hand from {Column}.", card.Column);

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
