using Act.Core.Model;

namespace Act.Core.Rules;

// Which deletions are worth stopping to ask about. Deleting is reversible — the archive can put the
// card back — so an "are you sure" on every strip would be ceremony on a gesture people use to tidy
// up. Two columns are different: **Executing** means an agent is running right now, and **Your turn**
// means one is parked mid-session waiting to be answered. Both end a live process, and that is the
// part the archive cannot restore. A card that has never launched loses nothing but a draft.
public static class CardDeleteConfirm
{
    public static bool IsRequired(BoardColumn column)
        => column is BoardColumn.Executing or BoardColumn.YourTurn;
}
