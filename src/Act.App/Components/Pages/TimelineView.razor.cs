using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// A card's history, read from the transitions it accumulated. What a row says is `TimelineEntry`'s;
// what is left here is finding the card and following it while the page is open.
public partial class TimelineView(BoardState board, IClock clock, NavigationManager navigation) : IDisposable
{
    private Card? card;

    private IReadOnlyList<TimelineEntry> entries = [];

    private (Guid Id, int Count, DateTime Day)? shown;

    [Parameter]
    public Guid CardId { get; set; }

    private IReadOnlyList<TimelineEntry> Entries => entries;

    protected override void OnInitialized() => board.Changed += OnChanged;

    // The card is resolved per navigation rather than once: the tabs move between a card's faces
    // without leaving the component, and the id is what changes.
    protected override void OnParametersSet() => Refresh();

    public void Dispose() => board.Changed -= OnChanged;

    private void OnChanged() => _ = InvokeAsync(() =>
    {
        Refresh();
        StateHasChanged();
    });

    // Rebuilt only when this card's history could have changed, not on every board write: `Changed`
    // fires once a second under a chatty session and replaces every card instance, so the key is the
    // card's content — transitions are append-only — plus the local date the stamps were cut against.
    // A reference check would never match and a rebuild per render re-sorts and re-formats rows for
    // news that belongs to other cards.
    private void Refresh()
    {
        card = board.Card(CardId);

        var key = card is { } current
            ? (current.Id, current.Transitions.Count, clock.Now.ToLocalTime().Date)
            : default((Guid, int, DateTime)?);

        if (key == shown)
            return;

        shown = key;
        entries = card is { } known ? TimelineEntry.For(known, clock.Now) : [];
    }

    // The board, or the archive for a card that is off it — see `CardExit`.
    private void Back() => navigation.NavigateTo(CardExit.Route(card));
}
