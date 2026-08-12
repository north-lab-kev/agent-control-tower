using Act.App.Resources;
using Act.Core.Model;

namespace Act.App.Cards;

// Where "back" goes from a card's page: wherever the card actually is. The board for a live task,
// the archive for one that is off it — landing on a board the card is not on reads as having lost
// it. Shared by the three faces so the header button, Escape and Cancel cannot disagree, and so the
// label always names the page it is about to open.
public static class CardExit
{
    public static string Route(Card? card) => card is { IsOnBoard: false } ? "/archive" : "/";

    public static string Label(Card? card)
        => card is { IsOnBoard: false } ? Strings.Archive_Title : Strings.Nav_BackToBoard;
}
