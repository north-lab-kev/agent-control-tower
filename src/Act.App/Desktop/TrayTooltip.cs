using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Desktop;

// What the tray icon says it is hovering over. Counted with `NotificationTrigger` rather than by
// column, so the tooltip, the toast and the blink can never disagree about what "the ball is in
// your court" means.
//
// Separated from the shell because it is the only part of the tray that is worth asserting on, and
// the shell itself cannot be constructed without Electron.
internal static class TrayTooltip
{
    public static string For(IEnumerable<Card> cards)
    {
        var waiting = cards.Count(NotificationTrigger.Wants);

        // Nothing waiting is not "0 tasks waiting" — a tray icon quietly naming the app is the
        // resting state, and a zero in a tooltip reads as a number worth checking.
        if (waiting is 0)
            return Strings.Shell_FullName;

        return Text.Format(
            Text.Plural(waiting, Strings.Tray_Tooltip_One, Strings.Tray_Tooltip_Many),
            Strings.Shell_FullName,
            waiting);
    }
}
