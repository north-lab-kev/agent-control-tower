using Act.Core.Model;

namespace Act.Core.Rules;

// The human-controlled half of the state model: which cards the user may pick up, and
// where they may drop them. Past the launch boundary a card moves only through automated
// transitions, so Executing neither lifts nor accepts drops.
//
// Your turn lifts, even though it is a machine column, because sign-off is the user's — but the
// only place it may land is Completed, and that landing is `CardCompletion`'s rather than a plain
// move: see the note there. Completed lifts for the mirror of it, the reopen, whose one landing is
// Your turn and whose rule is `CardReopen`.
public static class ManualMove
{
    public static bool CanDrag(BoardColumn column)
        => column is BoardColumn.Preparing
            or BoardColumn.Ready
            or BoardColumn.YourTurn
            or BoardColumn.Completed;

    public static bool IsAllowed(BoardColumn from, BoardColumn to) => (from, to) switch
    {
        (BoardColumn.Preparing, BoardColumn.Ready) => true,
        (BoardColumn.Ready, BoardColumn.Preparing) => true,
        _ => false,
    };
}
