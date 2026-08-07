using Act.Core.Model;

namespace Act.Core.Scheduling;

// How many cards may be running at once: everything past the launch boundary that is not merely
// awaiting sign-off — Executing, plus Your turn on any badge but `to review`.
//
// A card parked at a permission prompt still owns an agent process and its pty, so it costs what a
// working one costs. An `error` or `killed` card has no process left, and counting it anyway is
// deliberate: it is a failure the user has not dealt with, it is one Retry away from being live
// again, and a queue that kept launching past a growing pile of them would turn one broken task
// into twenty. The cap is the board's ceiling on work in flight, not a process count.
//
// `to review` is the one that costs nothing and blocks nothing: the turn is over, the session is
// finished, and all that is left is a signature.
public static class ConcurrencySlots
{
    public const int Minimum = 1;

    public const int Maximum = 20;

    public static int Clamp(int cap) => Math.Clamp(cap, Minimum, Maximum);

    public static bool Occupies(Card card)
        => card.IsOnBoard
            && (card.Column is BoardColumn.Executing
                || (card.Column is BoardColumn.YourTurn && card.Badge is not Badge.ReadyForReview));

    public static int Used(IEnumerable<Card> cards) => cards.Count(Occupies);

    public static bool HasRoom(IEnumerable<Card> cards, int cap) => Used(cards) < Clamp(cap);
}
