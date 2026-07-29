using Act.Core.Model;

namespace Act.Core.Rules;

// The human-controlled half of the state model: which cards the user may pick up, and
// where they may drop them. Past the launch boundary a card moves only through automated
// transitions, so machine columns neither lift nor accept drops.
public static class ManualMove
{
    public static bool CanDrag(BoardColumn column)
        => column is BoardColumn.Preparing or BoardColumn.Ready;

    public static bool IsAllowed(BoardColumn from, BoardColumn to) => (from, to) switch
    {
        (BoardColumn.Preparing, BoardColumn.Ready) => true,
        (BoardColumn.Ready, BoardColumn.Preparing) => true,
        _ => false,
    };
}
