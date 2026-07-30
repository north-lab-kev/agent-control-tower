using Act.Core.Model;

namespace Act.Core.Rules;

// Your turn → Completed: the one transition on the far side of the launch boundary that is the
// user's. It is deliberately neither of the other two kinds of move, which is why it gets its own
// predicate instead of being folded into one of them:
//
//   * not a `ManualMove` — nothing is dragged out of a machine column, so a Your turn card still
//     does not lift, and Completed is reached by an explicit action rather than by a drop;
//   * not a `RulesEngine` decision — no observed event may ever produce Completed, because a card
//     nobody has reviewed is not done.
//
// The column is the whole gate, not the badge: a card the user is looking at because it crashed or
// was killed is just as sign-off-able as one that finished cleanly, and gating on the review badge
// would leave those with no way out but a round trip through the terminal.
public static class CardCompletion
{
    public static bool CanComplete(Card card)
        => !card.IsDeleted && card.Column is BoardColumn.YourTurn;
}
