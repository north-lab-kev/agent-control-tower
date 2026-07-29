using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Board;

public partial class BoardView(
    BoardState board,
    DialogService dialogService,
    NavigationManager navigation) : IDisposable
{
    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

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
    private async Task OpenAsync(Card card)
    {
        if (card.Column is not (BoardColumn.Preparing or BoardColumn.Ready))
        {
            navigation.NavigateTo($"/card/{card.Id}/terminal");

            return;
        }

        await dialogService.OpenAsync<TaskDialog>(
            Strings.TaskDialog_EditTitle,
            new Dictionary<string, object?> { [nameof(TaskDialog.Card)] = card },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = false, CssClass = "act-dialog act-dialog-form" });
    }

    // Ready → Executing is ACT's action, not a drag: the board hands off to the session view,
    // which owns the terminal geometry the pty has to be sized with.
    private void LaunchAsync(Card card) => navigation.NavigateTo($"/card/{card.Id}/terminal");

    private static string Label(BoardColumn column) => column switch
    {
        BoardColumn.Preparing => Strings.Column_Preparing,
        BoardColumn.Ready => Strings.Column_Ready,
        BoardColumn.Executing => Strings.Column_Executing,
        BoardColumn.NeedsFeedback => Strings.Column_NeedsFeedback,
        BoardColumn.ToReview => Strings.Column_ToReview,
        BoardColumn.Completed => Strings.Column_Completed,
        _ => column.ToString(),
    };
}
