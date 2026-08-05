using Act.Core.Model;

namespace Act.Core.Rules;

// Your turn → Completed: the one transition on the far side of the launch boundary that is the
// user's. It is deliberately neither of the other two kinds of move, which is why it gets its own
// predicate instead of being folded into one of them:
//
//   * not a `ManualMove` — the card is dragged there, but reaching Completed stamps the sign-off and
//     ends the session, which `BoardState.MoveAsync` neither does nor should;
//   * not a `RulesEngine` decision — no observed event may ever produce Completed, because a card
//     nobody has reviewed is not done.
//
// The column is the whole gate, not the badge: a card the user is looking at because it crashed or
// was killed is just as sign-off-able as one that finished cleanly, and gating on the review badge
// would leave those with no way out but a round trip through the terminal.
public static class CardCompletion
{
    public static bool CanComplete(Card card)
        => card.IsOnBoard && card.Column is BoardColumn.YourTurn;

    public static bool CanCompleteInto(Card card, BoardColumn target)
        => target is BoardColumn.Completed && CanComplete(card);
}
