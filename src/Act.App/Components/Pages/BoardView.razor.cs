using Act.App.Cards;
using Act.App.Resources;
using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Pages;

public partial class BoardView(
    BoardState board,
    SessionLauncher launcher,
    QueueRunner queue,
    TerminalGeometry geometry,
    CardCompleter completer,
    CardReopener reopener,
    NotificationService notifications,
    NavigationManager navigation) : IAsyncDisposable
{
    // What the quiet chip costs: it is derived from a stamp rather than from an event, so nothing
    // pushes a re-render when it changes. Half a minute is finer than the thing it renders — a chip
    // counting whole minutes — and a board with no live session never ticks at all.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

    private readonly HashSet<Guid> launching = [];

    private readonly HashSet<Guid> signing = [];

    private readonly CancellationTokenSource leaving = new();

    private Card? dragging;

    [CascadingParameter(Name = "Density")]
    private BoardDensity Density { get; set; }

    [CascadingParameter(Name = "BlinkYourTurn")]
    private bool BlinkYourTurn { get; set; }

    private string DensityClass => Density is BoardDensity.Compact ? "compact" : "spacious";

    protected override void OnInitialized()
    {
        board.Changed += OnChanged;
        queue.Evaluated += OnChanged;

        _ = RefreshLoopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        board.Changed -= OnChanged;
        queue.Evaluated -= OnChanged;

        await leaving.CancelAsync();

        leaving.Dispose();
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(leaving.Token))
            {
                // Ready counts too now: a hold chip counting down to a usage reset is the same kind
                // of number as the quiet chip — derived from an instant, with nothing to push it.
                if (board.In(BoardColumn.Executing).Count > 0 || board.In(BoardColumn.Ready).Count > 0)
                    await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IReadOnlyList<Card> CardsIn(BoardColumn column) => board.In(column);

    // Read from the runner rather than computed here, so the chip a Ready card shows and the
    // decision the runner acts on are literally the same evaluation.
    private IReadOnlyDictionary<Guid, ReadyHold> Holds() => queue.Evaluate().Holds;

    private string ColumnClass(BoardColumn column)
    {
        if (dragging is not { } card || card.Column == column)
            return "col";

        return CanDrop(card, column) ? "col drop-ok" : "col drop-no";
    }

    private static bool CanDrop(Card card, BoardColumn column)
        => ManualMove.IsAllowed(card.Column, column)
            || CardCompletion.CanCompleteInto(card, column)
            || CardReopen.CanReopenInto(card, column);

    private void OnDragStartAsync(Card card) => dragging = card;

    private void OnDragEndAsync() => dragging = null;

    private async Task DropAsync(BoardColumn column)
    {
        var card = dragging;
        dragging = null;

        if (card is null)
            return;

        // The one drop that is not a move: sign-off stamps the card and ends its session, so it goes
        // through the completer rather than through `MoveAsync`.
        if (CardCompletion.CanCompleteInto(card, column))
        {
            await CompleteAsync(card);

            return;
        }

        // Nor is the mirror of it: taking the sign-off back un-stamps `completedAt`, which is the
        // retention clock, so it goes through the reopener for the same reason.
        if (CardReopen.CanReopenInto(card, column))
        {
            await ReopenAsync(card);

            return;
        }

        if (!ManualMove.IsAllowed(card.Column, column))
            return;

        await board.MoveAsync(card, column);
    }

    // Past the launch boundary a card *is* its session, so opening one goes to its terminal
    // rather than to the form — the form's fields are not the user's to change any more.
    private void OpenAsync(Card card) => navigation.NavigateTo(
        card.Column is BoardColumn.Preparing or BoardColumn.Ready
            ? $"/card/{card.Id}/edit"
            : $"/card/{card.Id}/terminal");

    private void OpenNewTask() => navigation.NavigateTo("/card/new");

    private bool IsLaunching(Card card) => launching.Contains(card.Id);

    // Ready → Executing happens right here: the session belongs to the registry, not to a view,
    // so the agent is started headless at a default geometry and the strip simply moves. Opening
    // the terminal later re-attaches to that process and sizes it to the real xterm.
    private async Task LaunchAsync(Card card)
    {
        if (!launching.Add(card.Id))
            return;

        try
        {
            var result = await launcher.LaunchAsync(card, geometry.Last);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    Summary = result.Waiting ? Strings.Session_NotLaunched : Strings.Session_LaunchFailed,
                    Detail = message,
                    Duration = 20000,
                });
            }
        }
        finally
        {
            launching.Remove(card.Id);
        }
    }

    private bool CanRetry(Card card) => launcher.CanRetry(card);

    // The board is the right home for Retry, not the session view: opening a failed card's terminal
    // already resumes it, so by the time you are looking at the rail the process is live again and
    // typing into it is your job, not ACT's.
    private async Task RetryAsync(Card card)
    {
        if (!launching.Add(card.Id))
            return;

        try
        {
            var result = await launcher.RetryAsync(card, geometry.Last);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    Summary = result.Waiting ? Strings.Session_NotLaunched : Strings.Session_RetryFailed,
                    Detail = message,
                    Duration = 20000,
                });
            }
        }
        finally
        {
            launching.Remove(card.Id);
        }
    }

    // Your turn → Completed, the other transition the user drives by hand across the launch boundary.
    // Guarded like the launch is, because ending the card's session is not something to do twice.
    private async Task CompleteAsync(Card card)
    {
        if (!signing.Add(card.Id))
            return;

        try
        {
            await completer.CompleteAsync(card);
        }
        finally
        {
            signing.Remove(card.Id);
        }
    }

    // Completed → Your turn. No session is started here: a reopened card gets its terminal back the
    // way every bound card does, by being opened.
    private async Task ReopenAsync(Card card)
    {
        if (!signing.Add(card.Id))
            return;

        try
        {
            await reopener.ReopenAsync(card);
        }
        finally
        {
            signing.Remove(card.Id);
        }
    }

    private static string Label(BoardColumn column) => CardVisuals.Column(column);
}
