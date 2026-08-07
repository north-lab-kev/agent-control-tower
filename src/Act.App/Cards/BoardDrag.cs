using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Cards;

// What a drop turns out to mean. Four, because a card crossing a column boundary is not always a
// move: reaching Completed stamps the sign-off and ends the session, and leaving it un-stamps the
// retention clock — neither is `BoardState.MoveAsync`'s to do. Deciding which of the four it is
// takes only the rules, so it happens here; carrying it out needs the completer and the reopener,
// so that stays with the view.
public enum DropAction
{
    Reorder,
    Move,
    Complete,
    Reopen,
}

public sealed record Drop(DropAction Action, Card Card, BoardColumn Column, Card? Target = null);

// The board's drag gesture as a state machine: what is lifted, what the cursor is over, which lanes
// would take it, and which edge the insertion marker sits on. Split out of `BoardView` because none
// of it needs a rendered component — every answer here is a function of two cards and the rules —
// and because the drop-target logic is the half of the board that used to be untestable.
public sealed class BoardDrag
{
    public Card? Lifted { get; private set; }

    public Card? Over { get; private set; }

    public bool IsActive => Lifted is not null;

    public bool IsLifted(Card card) => Lifted?.Id == card.Id;

    public void Start(Card card) => Lifted = card;

    public void End()
    {
        Lifted = null;
        Over = null;
    }

    // Hovering a strip is what arms the reorder; hovering the lane itself clears it, so the marker
    // never outlives the card it was drawn under.
    public void OverStrip(Card card)
    {
        if (Lifted is { } lifted && lifted.Id != card.Id)
            Over = card;
    }

    public void OverLane() => Over = null;

    // Whether a lane would take what is being dragged — the same three rules the drop itself asks,
    // so a lane cannot light up green for a drop that is then refused.
    public static bool Accepts(Card card, BoardColumn column)
        => ManualMove.IsAllowed(card.Column, column)
            || CardCompletion.CanCompleteInto(card, column)
            || CardReopen.CanReopenInto(card, column);

    public string ColumnClass(BoardColumn column)
    {
        if (Lifted is not { } card || card.Column == column)
            return "col";

        return Accepts(card, column) ? "col drop-ok" : "col drop-no";
    }

    // Which edge of a hovered strip the insertion line is drawn on — asked of the same rule that
    // will do the move, so the marker cannot promise a position the drop does not deliver.
    public string? EdgeOn(Card card, IEnumerable<Card> column)
    {
        if (Over?.Id != card.Id || Lifted is not { } lifted || lifted.Column != card.Column)
            return null;

        return CardOrder.LandsAfter(column, lifted, card) ? "drop-after" : "drop-before";
    }

    // Dropping on a strip is the same gesture as dropping on a lane, told apart by where it lands:
    // onto one of its own column-mates it takes that card's place, and anything else is the column
    // change it always was — a card crossing columns lands last, not wherever the cursor happened
    // to be.
    public Drop? OnStrip(Card target)
    {
        if (Lifted is not { } card)
            return null;

        if (card.Column != target.Column)
            return IntoLane(target.Column);

        return card.Id == target.Id ? null : new Drop(DropAction.Reorder, card, target.Column, target);
    }

    public Drop? IntoLane(BoardColumn column)
    {
        if (Lifted is not { } card)
            return null;

        if (CardCompletion.CanCompleteInto(card, column))
            return new Drop(DropAction.Complete, card, column);

        if (CardReopen.CanReopenInto(card, column))
            return new Drop(DropAction.Reopen, card, column);

        return ManualMove.IsAllowed(card.Column, column)
            ? new Drop(DropAction.Move, card, column)
            : null;
    }
}
