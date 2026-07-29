using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Board;

public partial class BoardView(BoardState board, DialogService dialogService) : IDisposable
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

    private async Task OpenAsync(Card card)
        => await dialogService.OpenAsync<TaskDialog>(
            Strings.TaskDialog_EditTitle,
            new Dictionary<string, object?> { [nameof(TaskDialog.Card)] = card },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = false, CssClass = "act-dialog act-dialog-form" });

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
