using Act.Core.Model;

namespace Act.Core.Rules;

public static class CardReopen
{
    public static bool CanReopen(Card card)
        => card.IsOnBoard && card.Column is BoardColumn.Completed;

    public static bool CanReopenInto(Card card, BoardColumn target)
        => target is BoardColumn.YourTurn && CanReopen(card);
}
