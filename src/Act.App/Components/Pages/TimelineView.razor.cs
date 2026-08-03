using System.Globalization;
using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// A card's history, read from the transitions it accumulated. Oldest first, because a timeline that
// reads downwards is the one the eye follows — and because the connecting rail only makes sense in
// the direction the work actually went.
public partial class TimelineView(BoardState board, IClock clock, NavigationManager navigation) : IDisposable
{
    private Card? card;

    [Parameter]
    public Guid CardId { get; set; }

    private IReadOnlyList<Entry> Entries
    {
        get
        {
            if (card is null)
                return [];

            var now = clock.Now;

            return [.. card.Transitions.OrderBy(transition => transition.At).Select(transition => Entry.From(transition, now))];
        }
    }

    protected override void OnInitialized() => board.Changed += OnChanged;

    // The card is resolved per navigation rather than once: the tabs move between a card's faces
    // without leaving the component, and the id is what changes.
    protected override void OnParametersSet() => card = board.Card(CardId);

    public void Dispose() => board.Changed -= OnChanged;

    private void OnChanged() => _ = InvokeAsync(() =>
    {
        card = board.Card(CardId);

        StateHasChanged();
    });

    private void BackToBoard() => navigation.NavigateTo("/");

    // A row of the rail. `RailClass` reuses the badge tokens because it is the same signal: the dot
    // beside an entry should be the colour the badge was when it happened.
    private sealed record Entry(
        DateTimeOffset At,
        string Clock,
        string Text,
        BoardColumn? Column,
        string RailClass)
    {
        public static Entry From(Transition transition, DateTimeOffset now) => new(
            transition.At,
            Stamp(transition.At, now),
            Text: Wording(transition),
            // Suppressed for a row that has no reason of its own: the wording already *is* the
            // column, and "Entered Ready → Ready" reads like a bug.
            Column: transition.Reason is null ? null : transition.Column,
            RailClass: CardVisuals.BadgeClass(transition.Badge) ?? RailFor(transition.Column));

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

        // Rows written before `TransitionReason` existed carry neither a reason nor a note, and a
        // blank line looks like a defect rather than like history. All such a row actually tells us
        // is that the card entered a column, so that is what it says.
        private static string Wording(Transition transition)
        {
            var wording = TransitionText.For(transition);

            if (wording.Length > 0)
                return wording;

            // Qualified: the record's own `Text` member would otherwise shadow the helper class.
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
}
