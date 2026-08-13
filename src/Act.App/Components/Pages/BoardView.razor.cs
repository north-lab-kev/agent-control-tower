using System.Globalization;
using Act.App.Cards;
using Act.App.Components.Shared;
using Act.App.Notifications;
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
    TitleBackfill backfill,
    SessionLauncher launcher,
    QueueRunner queue,
    TerminalGeometry geometry,
    CardCompleter completer,
    CardReopener reopener,
    CardDeletion deletion,
    DialogService dialogService,
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

    private readonly HashSet<Guid> deleting = [];

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
        backfill.Changed += OnChanged;
        queue.Evaluated += OnChanged;
        settings.Changed += OnSettingsChanged;

        _ = RefreshLoopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        board.Changed -= OnChanged;
        backfill.Changed -= OnChanged;
        queue.Evaluated -= OnChanged;
        settings.Changed -= OnSettingsChanged;

        await leaving.CancelAsync();

        leaving.Dispose();
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnSettingsChanged()
    {
        pickable = null;

        OnChanged();
    }

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

    // Everything a column head needs from one `board.In` — the sort behind it runs once per column
    // per render instead of once per question, and the hidden-attention count is the difference of
    // two counts rather than a second match of every card against the query.
    private ColumnFace FaceOf(BoardColumn column)
    {
        var all = board.In(column);
        var shown = CardSearch.Filter(all, query);

        if (!Filtering)
            return new ColumnFace(shown, shown.Count.ToString(CultureInfo.CurrentCulture), Hidden: 0);

        var hidden = all.Count(Attention) - shown.Count(Attention);

        return new ColumnFace(shown, $"{shown.Count}/{all.Count}", hidden);
    }

    private static bool Attention(Card card) => card.NeedsAttention;

    private sealed record ColumnFace(IReadOnlyList<Card> Cards, string Count, int Hidden);

    private int ArchivedMatches => Filtering ? CardSearch.Filter(board.Archived, query).Count : 0;

    private string ArchiveHref => $"/archive?q={Uri.EscapeDataString(query)}";

    // Read from the runner rather than computed here, so the chip a Ready card shows and the
    // decision the runner acts on are literally the same evaluation — the one the last pass made,
    // not a fresh one per render. `queue.Evaluated` is subscribed above, so a pass that changes a
    // hold redraws the board that shows it.
    private IReadOnlyDictionary<Guid, ReadyHold> Holds() => queue.Latest.Holds;

    // Answered before the column is sorted: `EdgeOn` is null for every strip but the hovered one,
    // and the board re-renders often enough that a sort per strip per render is real money.
    private string? DropEdge(Card card)
        => drag.Over?.Id == card.Id ? drag.EdgeOn(card, board.In(card.Column)) : null;

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

    private IReadOnlyList<TaskTemplate>? pickable;

    // The default is left out: the button itself is what starts a task from it, so listing it in the
    // button's own menu offers the same thing twice. Cached because `settings.Templates` deep-copies
    // and sorts on every call while templates only change on `settings.Changed`, which clears this.
    private IReadOnlyList<TaskTemplate> PickableTemplates
        // The default is excluded because the button *is* it; Quick question is not, and that is the
        // point — one click from the board is the whole reason it is a template rather than a switch.
        => pickable ??= [.. settings.Templates.Where(template => !template.IsDefault)];

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
                notifications.Toast(
                    result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    result.Waiting ? Strings.Session_NotLaunched : Strings.Session_LaunchFailed,
                    message);
            }
        }
        finally
        {
            launching.Remove(card.Id);
        }
    }

    private bool CanRetry(Card card) => launcher.CanRetry(card);

    private bool IsDeleting(Card card) => deleting.Contains(card.Id);

    // Delete from the strip, so tidying the board does not cost a trip to the task page and back. What it
    // asks first is `CardDeletePrompt`'s, shared with that page so the same delete cannot warn in one
    // place and go quietly in the other.
    //
    // The follow-ups question is the *same* dialog the task page opens, deliberately — it is a genuine
    // choice rather than an "are you sure", and a card whose children silently outlived it is the kind
    // of orphan the board is supposed to make impossible to create by accident.
    private async Task DeleteAsync(Card card)
    {
        if (!deleting.Add(card.Id))
            return;

        try
        {
            if (!await CardDeletePrompt.ConfirmedAsync(dialogService, card))
                return;

            var followUps = LiveFollowUpsOf(card);
            var includeChildren = false;

            if (followUps.Count > 0)
            {
                var choice = await dialogService.OpenAsync<ArchiveFollowUpsDialog>(
                    Strings.Task_Delete,
                    new Dictionary<string, object?>
                    {
                        [nameof(ArchiveFollowUpsDialog.Card)] = card,
                        [nameof(ArchiveFollowUpsDialog.FollowUps)] = followUps,
                    },
                    new DialogOptions { Width = "460px", CloseDialogOnOverlayClick = true, CssClass = "act-dialog" });

                // Null when dismissed with the X or the overlay, which means the same as Cancel.
                if (choice is not (FollowUpChoice.WithFollowUps or FollowUpChoice.KeepFollowUps))
                    return;

                includeChildren = choice is FollowUpChoice.WithFollowUps;
            }

            await deletion.DeleteAsync(card, includeChildren, leaving.Token);
        }
        catch (InvalidOperationException error)
        {
            notifications.Toast(NotificationSeverity.Error, Strings.Task_DeleteFailed, error.Message);
        }
        finally
        {
            deleting.Remove(card.Id);
        }
    }

    private IReadOnlyList<Card> LiveFollowUpsOf(Card card)
        => [.. board.ChildrenOf(card).Where(child => !child.IsDeleted)];

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
                notifications.Toast(
                    result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    result.Waiting ? Strings.Session_NotLaunched : Strings.Session_RetryFailed,
                    message);
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
