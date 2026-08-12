using Act.Core.Model;

namespace Act.Core.Rules;

public static class NotificationTrigger
{
    public static bool Wants(Card card)
        => card.IsOnBoard && card.Column is BoardColumn.YourTurn && card.NeedsAttention;
}
