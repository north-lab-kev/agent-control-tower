using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Sessions;

// Your turn → Completed. The mirror of `SessionLauncher`: the two transitions the user drives across
// the launch boundary, and neither of them is the rules engine's to make.
//
// The card is stamped and persisted *before* the terminal is torn down, and that order is deliberate:
// the engine governs only the machine region, so a card that is already Completed absorbs anything the
// dying session still emits instead of being dragged back into Your turn by it.
public sealed class CardCompleter(BoardState board, SessionRegistry registry, IClock clock)
{
    public bool CanComplete(Card card) => CardCompletion.CanComplete(card);

    public async Task<bool> CompleteAsync(Card card, CancellationToken cancellationToken = default)
    {
        if (!CanComplete(card))
            return false;

        card.Column = BoardColumn.Completed;
        card.Badge = null;
        card.CompletedAt = clock.Now;
        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = BoardColumn.Completed,
            Reason = TransitionReason.CompletedByHand,
        });

        await board.UpdateAsync(card, cancellationToken);

        // The session lives for exactly as long as the card is active, so signing the work off is
        // what ends it. The binding stays on the card: the transcript is what a reopen resumes.
        await registry.EndAsync(card.Id);

        return true;
    }
}
