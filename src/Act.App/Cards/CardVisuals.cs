using Act.App.Resources;
using Act.Core.Model;

namespace Act.App.Cards;

// The column and badge vocabulary the UI shares. It was written out three times — the flight strip,
// the session bar, and now the timeline — which is one time too many for a mapping whose whole job
// is that the same signal looks the same everywhere.
//
// Only the common part lives here. The strip's extras stay with the strip, because they are about
// that surface rather than about the signal: a Completed card shows `done` where it has no badge,
// and a quiet session is stated beside its badge rather than in it.
public static class CardVisuals
{
    public static string Column(BoardColumn column) => column switch
    {
        BoardColumn.Preparing => Strings.Column_Preparing,
        BoardColumn.Ready => Strings.Column_Ready,
        BoardColumn.Executing => Strings.Column_Executing,
        BoardColumn.YourTurn => Strings.Column_YourTurn,
        BoardColumn.Completed => Strings.Column_Completed,
        _ => column.ToString(),
    };

    // The `b-*` classes every surface's CSS defines against the same `--act-*` tokens.
    public static string? BadgeClass(Badge? badge) => badge switch
    {
        Badge.Running or Badge.Compacting => "b-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "b-wait",
        Badge.Error or Badge.Killed => "b-err",
        Badge.ReadyForReview => "b-review",
        _ => null,
    };

    public static string? BadgeText(Badge? badge) => badge switch
    {
        Badge.Running => Strings.Badge_Running,
        Badge.Compacting => Strings.Badge_Compacting,
        Badge.NeedsPermission => Strings.Badge_NeedsPermission,
        Badge.NeedsAnswer => Strings.Badge_NeedsAnswer,
        Badge.Error => Strings.Badge_Error,
        Badge.Killed => Strings.Badge_Killed,
        Badge.ReadyForReview => Strings.Badge_ToReview,
        _ => null,
    };
}
