using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.Components.Board;

// One flight strip. What it *says* is `StripFace`'s — the badge, the rail, the chips, the metrics —
// and what is left here is what only a component can do: the parameters, the gestures, and which of
// the two densities is being drawn.
public partial class FlightStrip(IClock clock)
{
    [Parameter, EditorRequired]
    public Card Card { get; set; } = default!;

    [Parameter]
    public BoardDensity Density { get; set; }

    [Parameter]
    public bool Blink { get; set; } = true;

    [Parameter]
    public EventCallback<Card> OnOpen { get; set; }

    [Parameter]
    public EventCallback<Card> OnLaunch { get; set; }

    [Parameter]
    public EventCallback<Card> OnRetry { get; set; }

    // Whether this card can be retried, decided by the board — the answer depends on whether a
    // session is live, which is registry state the strip has no business reaching for.
    [Parameter]
    public bool Retriable { get; set; }

    [Parameter]
    public EventCallback<Card> OnDragStart { get; set; }

    [Parameter]
    public EventCallback OnDragEnd { get; set; }

    [Parameter]
    public EventCallback<Card> OnDragOver { get; set; }

    [Parameter]
    public EventCallback<Card> OnDrop { get; set; }

    [Parameter]
    public bool IsDragging { get; set; }

    // Which edge the insertion marker sits on, or null when nothing is hovering this strip. Decided
    // by the board, which is the only side that knows what is being dragged.
    [Parameter]
    public string? DropEdge { get; set; }

    [Parameter]
    public bool IsLaunching { get; set; }

    // Why this Ready card is not running, decided by the board in one pass over every card — the
    // same pass the queue runner launches from, so a strip cannot claim a card is queued while it
    // is starting.
    [Parameter]
    public ReadyHold? Hold { get; set; }

    // The card this one was spawned from, resolved by the board for the same reason `Hold` is: the
    // strip holds one card and the lineage marker needs the *parent's* number, which only something
    // looking at the whole store can supply.
    [Parameter]
    public Card? Parent { get; set; }

    // Recomputed per render on purpose: the quiet chip and the usage countdown are derived from an
    // instant, so nothing pushes a change when they advance — the board's refresh tick is what makes
    // the number move.
    private StripFace Face => StripFace.Of(Card, Hold, clock.Now, Parent);

    private bool Draggable => ManualMove.CanDrag(Card.Column);

    private bool IsCompact => Density is BoardDensity.Compact;

    // Both densities: a compact board is the one you scan to start work from, so the button that
    // starts it cannot be the thing density trades away. It goes on the second line as an icon,
    // beside the path, which leaves the first line's badge exactly the width it had.
    //
    // Offered in Preparing as well as Ready: a card you have finished drafting is one you want to
    // start, and making that cost a drag first was ceremony. The board promotes it — see
    // `BoardView.LaunchAsync` — so Ready is still passed through rather than skipped.
    private bool CanLaunch => Card.Column is BoardColumn.Preparing or BoardColumn.Ready;

    private bool CanRetry => Retriable;

    private Task OpenAsync() => OnOpen.InvokeAsync(Card);

    private Task LaunchAsync() => OnLaunch.InvokeAsync(Card);

    private Task RetryAsync() => OnRetry.InvokeAsync(Card);

    private Task OnDragStartAsync() => Draggable ? OnDragStart.InvokeAsync(Card) : Task.CompletedTask;

    private Task OnDragEndAsync() => OnDragEnd.InvokeAsync();

    private Task OnDragOverAsync() => OnDragOver.InvokeAsync(Card);

    private Task OnDropAsync() => OnDrop.InvokeAsync(Card);

    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => args.Key is "Enter" or " " ? OnOpen.InvokeAsync(Card) : Task.CompletedTask;
}
