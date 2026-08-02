using Act.Core.Model;

namespace Act.Core.Rules;

// The order cards sit in inside a column — and, for Ready, the order the queue takes them in. The
// board and the runner read the same sequence, because a queue that runs in an order the board does
// not show is a queue nobody can predict.
//
// **FIFO until someone says otherwise.** A card arriving in a column is stamped past everything
// already there, so a board nobody has reordered is in arrival order; a manual drag resequences the
// column and that sequence is the one that stands from then on. `Number` is the tiebreak, which is
// what keeps cards stored before ordering existed — all of them at zero — in the order they were
// created.
public static class CardOrder
{
    public static IReadOnlyList<Card> Sort(IEnumerable<Card> cards)
        => [.. cards.OrderBy(card => card.Order).ThenBy(card => card.Number)];

    // The stamp for a card landing in `column`: past every card already in it. The arriving card is
    // excluded because it may already be sitting there under its previous position.
    public static int Last(IEnumerable<Card> column, Card arriving)
        => column
            .Where(card => card.Id != arriving.Id)
            .Select(card => card.Order)
            .DefaultIfEmpty(0)
            .Max() + 1;

    // Dropping onto a card takes its place: dragged down, the card lands *after* the one under the
    // cursor; dragged up, before it. That is the reading every list reorder has, and it is the one
    // the insertion marker draws.
    public static bool LandsAfter(IEnumerable<Card> column, Card moved, Card target)
    {
        var ordered = Sort(column);

        return IndexOf(ordered, moved) < IndexOf(ordered, target);
    }

    // Resequences the column and hands back only the cards whose position actually changed, so a
    // reorder writes the few rows it moved rather than the whole lane.
    public static IReadOnlyList<Card> Move(IEnumerable<Card> column, Card moved, Card target)
    {
        var ordered = new List<Card>(Sort(column));

        var from = IndexOf(ordered, moved);
        var to = IndexOf(ordered, target);

        if (from < 0 || to < 0 || from == to)
            return [];

        ordered.RemoveAt(from);
        ordered.Insert(to, moved);

        var changed = new List<Card>();

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Order == index + 1)
                continue;

            ordered[index].Order = index + 1;

            changed.Add(ordered[index]);
        }

        return changed;
    }

    private static int IndexOf(IReadOnlyList<Card> ordered, Card card)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Id == card.Id)
                return index;
        }

        return -1;
    }
}
