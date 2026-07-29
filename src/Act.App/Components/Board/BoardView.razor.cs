using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Board;

public partial class BoardView(ICardStore cards)
{
    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

    private IReadOnlyList<Card> board = [];

    [CascadingParameter(Name = "Density")]
    private BoardDensity Density { get; set; }

    private string DensityClass => Density is BoardDensity.Compact ? "compact" : "spacious";

    protected override async Task OnInitializedAsync()
        => board = await cards.GetAllAsync();

    private IReadOnlyList<Card> CardsIn(BoardColumn column)
        => [.. board.Where(card => card.Column == column)];

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
