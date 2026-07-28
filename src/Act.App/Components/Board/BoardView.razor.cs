using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Board;

public partial class BoardView
{
    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

    [CascadingParameter(Name = "Density")]
    private BoardDensity Density { get; set; }

    private string DensityClass => Density is BoardDensity.Compact ? "compact" : "spacious";

    private static IReadOnlyList<Card> CardsIn(BoardColumn column)
        => [.. SampleBoard.Cards.Where(c => c.Column == column)];

    private static string Label(BoardColumn column) => column switch
    {
        BoardColumn.NeedsFeedback => "Needs feedback",
        BoardColumn.ToReview => "To review",
        _ => column.ToString(),
    };
}
