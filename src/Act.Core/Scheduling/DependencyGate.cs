using Act.Core.Model;

namespace Act.Core.Scheduling;

// `dependsOn` ordering. A card waits until every prerequisite has been signed off — Completed, not
// merely finished a turn, for the same reason the folder guard reads the column: the work is not
// done until a human says so.
//
// A prerequisite the store no longer has **counts as satisfied**. The alternative is a card that
// can never launch and says nothing about why, which is a worse answer than starting work whose
// predecessor was deleted on purpose.
public static class DependencyGate
{
    public static Card? Blocking(Card card, IEnumerable<Card> cards)
    {
        if (card.DependsOn.Count == 0)
            return null;

        var byId = cards.ToDictionary(other => other.Id);

        foreach (var id in card.DependsOn)
        {
            if (byId.TryGetValue(id, out var prerequisite)
                && prerequisite.Column is not BoardColumn.Completed
                && !prerequisite.IsDeleted)
                return prerequisite;
        }

        return null;
    }
}
