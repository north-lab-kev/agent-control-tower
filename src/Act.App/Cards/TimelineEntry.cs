using System.Globalization;
using Act.App.Resources;
using Act.Core.Model;

namespace Act.App.Cards;

// One row of a card's history rail. Split out of `TimelineView` because the two decisions in it are
// worth being sure of and neither needs a component: how much of a date to show, and what a row says
// when the transition that produced it carries no reason.
public sealed record TimelineEntry(
    DateTimeOffset At,
    string Clock,
    string Text,
    BoardColumn? Column,
    string RailClass)
{
    // Oldest first, because a timeline that reads downwards is the one the eye follows — and because
    // the connecting rail only makes sense in the direction the work actually went.
    public static IReadOnlyList<TimelineEntry> For(Card card, DateTimeOffset now)
        => [.. card.Transitions.OrderBy(transition => transition.At).Select(transition => Of(transition, now))];

    public static TimelineEntry Of(Transition transition, DateTimeOffset now) => new(
        transition.At,
        Stamp(transition.At, now),
        Wording(transition),
        // Suppressed for a row that has no reason of its own: the wording already *is* the column,
        // and "Entered Ready → Ready" reads like a bug.
        transition.Reason is null ? null : transition.Column,
        // `RailClass` reuses the badge tokens because it is the same signal: the dot beside an entry
        // should be the colour the badge was when it happened.
        CardVisuals.BadgeClass(transition.Badge) ?? RailFor(transition.Column));

    // As much of the date as it takes to place the row and no more: today needs only a clock, this
    // year needs no year, and anything older gets the short date.
    private static string Stamp(DateTimeOffset at, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = at.ToLocalTime();
        var today = now.ToLocalTime();
        var clock = local.ToString("HH:mm:ss", culture);

        if (local.Date == today.Date)
            return clock;

        var date = local.Year == today.Year
            ? local.ToString(culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), culture)
            : local.ToString("d", culture);

        return $"{date} {clock}";
    }

    // Rows written before `TransitionReason` existed carry neither a reason nor a note, and a blank
    // line looks like a defect rather than like history. All such a row actually tells us is that the
    // card entered a column, so that is what it says.
    private static string Wording(Transition transition)
    {
        var wording = TransitionText.For(transition);

        if (wording.Length > 0)
            return wording;

        return transition.Column is { } column
            ? Resources.Text.Format(Strings.Timeline_EnteredColumn, CardVisuals.Column(column))
            : string.Empty;
    }

    // A hand move has no badge, so the column it landed in supplies the colour instead.
    private static string RailFor(BoardColumn? column) => column switch
    {
        BoardColumn.Ready => "b-ready",
        BoardColumn.Completed => "b-done",
        _ => "b-plan",
    };
}
