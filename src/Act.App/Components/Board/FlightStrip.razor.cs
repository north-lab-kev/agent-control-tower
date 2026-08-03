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

    // Recomputed per render on purpose: the quiet chip and the usage countdown are derived from an
    // instant, so nothing pushes a change when they advance — the board's refresh tick is what makes
    // the number move.
    private StripFace Face => StripFace.Of(Card, Hold, clock.Now);

    private bool Draggable => ManualMove.CanDrag(Card.Column);

    private bool IsCompact => Density is BoardDensity.Compact;

    // Detailed only: the compact strip has no room for either without pushing the badge out, and the
    // badge is the signal the density exists to preserve.
    private bool CanLaunch => Card.Column is BoardColumn.Ready && !IsCompact;

    private bool CanRetry => Retriable && !IsCompact;

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
