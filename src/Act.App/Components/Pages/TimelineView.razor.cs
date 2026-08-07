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

    [Parameter]
    public Guid CardId { get; set; }

    private IReadOnlyList<TimelineEntry> Entries
        => card is null ? [] : TimelineEntry.For(card, clock.Now);

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

    // The board, or the archive for a card that is off it — see `CardExit`.
    private void Back() => navigation.NavigateTo(CardExit.Route(card));
}
