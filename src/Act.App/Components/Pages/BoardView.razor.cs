using Act.App.Cards;
using Act.App.Resources;
using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Pages;

public partial class BoardView(
    BoardState board,
    SessionLauncher launcher,
    NotificationService notifications,
    NavigationManager navigation) : IDisposable
{
    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

    private readonly HashSet<Guid> launching = [];

    private Card? dragging;

    [CascadingParameter(Name = "Density")]
    private BoardDensity Density { get; set; }

    private string DensityClass => Density is BoardDensity.Compact ? "compact" : "spacious";

    protected override void OnInitialized() => board.Changed += OnChanged;

    public void Dispose() => board.Changed -= OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private IReadOnlyList<Card> CardsIn(BoardColumn column) => board.In(column);

    private string ColumnClass(BoardColumn column)
    {
        if (dragging is not { } card || card.Column == column)
            return "col";

        return ManualMove.IsAllowed(card.Column, column) ? "col drop-ok" : "col drop-no";
    }

    private void OnDragStartAsync(Card card) => dragging = card;

    private void OnDragEndAsync() => dragging = null;

    private async Task DropAsync(BoardColumn column)
    {
        var card = dragging;
        dragging = null;

        if (card is null || !ManualMove.IsAllowed(card.Column, column))
            return;

        await board.MoveAsync(card, column);
    }

    // Past the launch boundary a card *is* its session, so opening one goes to its terminal
    // rather than to the form — the form's fields are not the user's to change any more.
    private void OpenAsync(Card card) => navigation.NavigateTo(
        card.Column is BoardColumn.Preparing or BoardColumn.Ready
            ? $"/card/{card.Id}/edit"
            : $"/card/{card.Id}/terminal");

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
            var result = await launcher.LaunchAsync(card, TerminalSize.Default);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = Strings.Session_LaunchFailed,
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

    private static string Label(BoardColumn column) => CardVisuals.Column(column);
}
