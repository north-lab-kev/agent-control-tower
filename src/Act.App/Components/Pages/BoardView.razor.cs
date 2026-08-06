using System.Globalization;
using Act.App.Cards;
using Act.App.Resources;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;

namespace Act.App.Components.Pages;

public partial class BoardView(
    BoardState board,
    SessionLauncher launcher,
    QueueRunner queue,
    TerminalGeometry geometry,
    CardCompleter completer,
    CardReopener reopener,
    NotificationService notifications,
    UserSettingsService settings,
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

    // The gesture, and the whole of what a drop would mean — see `BoardDrag`. The view is left with
    // carrying the decision out, which is the only part that needs the board and the two services.
    private readonly BoardDrag drag = new();

    private string query = string.Empty;

    [CascadingParameter(Name = "Density")]
    private BoardDensity Density { get; set; }

    [CascadingParameter(Name = "BlinkYourTurn")]
    private bool BlinkYourTurn { get; set; }

    private string DensityClass => Density is BoardDensity.Compact ? "compact" : "detailed";

    private bool Paused => settings.AutoExecutionPaused;

    private string AutoExecIcon => Paused ? "pause_circle" : "play_circle";

    private string AutoExecTip => Paused ? Strings.Shell_AutoExecPaused_Tip : Strings.Shell_AutoExecOn_Tip;

    // The green glyph is what says the queue is live, so a paused board loses it and takes the amber
    // of every other "waiting on you" surface.
    private string AutoExecClass => Paused ? "statchip paused" : "statchip live";

    private string DensityIcon => Density is BoardDensity.Compact ? "density_small" : "density_medium";

    // Says the mode you are in, not the one you would get: the board in front of you is the answer,
    // and a button that named the other one would disagree with it.
    private string DensityText => Density is BoardDensity.Compact
        ? Strings.Settings_Density_Compact
        : Strings.Settings_Density_Detailed;

    private void ToggleAutoExecution() => settings.SetAutoExecutionPaused(!Paused);

    private void ToggleDensity() => settings.SetDensity(
        Density is BoardDensity.Compact ? BoardDensity.Detailed : BoardDensity.Compact);

    protected override void OnInitialized()
    {
        board.Changed += OnChanged;
        queue.Evaluated += OnChanged;
        settings.Changed += OnChanged;

        _ = RefreshLoopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        board.Changed -= OnChanged;
        queue.Evaluated -= OnChanged;
        settings.Changed -= OnChanged;

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

    private bool Filtering => CardSearch.IsActive(query);

    private IReadOnlyList<Card> CardsIn(BoardColumn column) => CardSearch.Filter(board.In(column), query);

    // Reads `shown/total` while a query is live: the filter narrows what is drawn, and the header is
    // where it admits to it.
    private string Count(BoardColumn column, int shown) => Filtering
        ? $"{shown}/{board.In(column).Count}"
        : shown.ToString(CultureInfo.CurrentCulture);

    private int HiddenAttention(BoardColumn column) => Filtering
        ? board.In(column).Count(card => card.NeedsAttention && !CardSearch.Matches(card, query))
        : 0;

    private int ArchivedMatches => Filtering ? CardSearch.Filter(board.Archived, query).Count : 0;

    private string ArchiveHref => $"/archive?q={Uri.EscapeDataString(query)}";

    // Read from the runner rather than computed here, so the chip a Ready card shows and the
    // decision the runner acts on are literally the same evaluation — the one the last pass made,
    // not a fresh one per render. `queue.Evaluated` is subscribed above, so a pass that changes a
    // hold redraws the board that shows it.
    private IReadOnlyDictionary<Guid, ReadyHold> Holds() => queue.Latest.Holds;

    private string? DropEdge(Card card) => drag.EdgeOn(card, board.In(card.Column));

    // Both drops decide first and clear the gesture second, so the state is gone before anything
    // awaits — a drop that takes a moment must not leave a strip looking lifted.
    private Task DropOnAsync(Card target) => ApplyAsync(drag.OnStrip(target));

    private Task DropIntoAsync(BoardColumn column) => ApplyAsync(drag.IntoLane(column));

    private async Task ApplyAsync(Drop? drop)
    {
        drag.End();

        if (drop is not { } decided)
            return;

        switch (decided.Action)
        {
            case DropAction.Reorder:
                await board.ReorderAsync(decided.Card, decided.Target!);

                break;

            case DropAction.Complete:
                await CompleteAsync(decided.Card);

                break;

            case DropAction.Reopen:
                await ReopenAsync(decided.Card);

                break;

            default:
                await board.MoveAsync(decided.Card, decided.Column);

                break;
        }
    }

    private void OpenAsync(Card card) => navigation.NavigateTo(CardRoute.For(card));

    // The default is left out: the button itself is what starts a task from it, so listing it in the
    // button's own menu offers the same thing twice.
    private IReadOnlyList<TaskTemplate> PickableTemplates
        => [.. settings.Templates.Where(template => !template.IsDefault)];

    private void OpenNewTask() => navigation.NavigateTo("/card/new");

    // The item is null when the button itself was clicked rather than one of its menu entries, which
    // is the plain "new task from the default template" the board has always had.
    private void OnNewTaskPicked(RadzenSplitButtonItem? item)
        => navigation.NavigateTo(item?.Value is { Length: > 0 } id ? $"/card/new?template={id}" : "/card/new");

    private bool IsLaunching(Card card) => launching.Contains(card.Id);

    // Ready → Executing happens right here: the session belongs to the registry, not to a view,
    // so the agent is started headless at a default geometry and the strip simply moves. Opening
    // the terminal later re-attaches to that process and sizes it to the real xterm.
    //
    // A Preparing card is promoted first rather than launched where it stands: the launcher still
    // refuses Preparing outright — see `SessionLauncher.CanLaunch` — so the button does the drag for
    // you, and a launch that is then refused leaves the card in Ready, exactly where the drag alone
    // would have left it.
    private async Task LaunchAsync(Card card)
    {
        if (!launching.Add(card.Id))
            return;

        try
        {
            if (card.Column is BoardColumn.Preparing)
                await board.MoveAsync(card, BoardColumn.Ready);

            var result = await launcher.LaunchAsync(card, geometry.Last);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    Summary = result.Waiting ? Strings.Session_NotLaunched : Strings.Session_LaunchFailed,
                    Detail = message,
                    Duration = 5000,
                });
            }
        }
        finally
        {
            launching.Remove(card.Id);
        }
    }

    private bool CanRetry(Card card) => launcher.CanRetry(card);

    // Off `BoardState` rather than the column being drawn, because a parent is very often in a
    // different one — the whole point of a follow-up is that it outlives the turn that asked for it.
    private Card? ParentOf(Card card)
        => card.ParentId is { } parentId ? board.Card(parentId) : null;

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
                    Duration = 5000,
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
