using Act.Core.Model;

namespace Act.App.Cards;

// Which of a card's two faces opening it lands on. Past the launch boundary a card *is* its session,
// so it goes to the terminal rather than to the form — the form's fields are not the user's to change
// any more.
//
// Shared rather than repeated, because it now has more than one caller: the board opens a card, and
// so does a lineage link on the session rail. Two copies of this rule would eventually disagree about
// where a Ready card goes.
public static class CardRoute
{
    public static string For(Card card)
        => card.Column is BoardColumn.Preparing or BoardColumn.Ready
            ? $"/card/{card.Id}/edit"
            : $"/card/{card.Id}/terminal";
}
