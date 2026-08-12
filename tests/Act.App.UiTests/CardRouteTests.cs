using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The one rule for which face opening a card lands on, pinned directly: it has two callers — the
// board and the session rail's lineage links — and until now only the board's navigation theory
// covered it, so a change would have failed a board test without naming the shared type both
// depend on.
public class CardRouteTests
{
    [Theory]
    [InlineData(BoardColumn.Preparing, "edit")]
    [InlineData(BoardColumn.Ready, "edit")]
    [InlineData(BoardColumn.Executing, "terminal")]
    [InlineData(BoardColumn.YourTurn, "terminal")]
    [InlineData(BoardColumn.Completed, "terminal")]
    public void Opening_a_card_routes_to_the_face_its_state_chooses(BoardColumn column, string face)
    {
        var card = new Card { Title = "Rename the widget", Column = column, WorkingDir = "/dev/act" };

        CardRoute.For(card).Should().Be($"/card/{card.Id}/{face}");
    }
}
