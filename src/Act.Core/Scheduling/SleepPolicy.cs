using Act.Core.Model;

namespace Act.Core.Scheduling;

// Whether the machine must be kept awake. The setting says the user *wants* the hold; this says
// whether there is anything to hold it for — an idle board is safe to sleep, and a hold taken for
// the life of the process is a laptop that never suspends because ACT is open.
//
// Two things are worth protecting. **A live agent process**, because sleeping mid-turn suspends
// one. And **auto-work that has not started yet**, because the whole point of the overnight queue
// is a machine still awake when the window opens — which is why a Ready card counts only when it
// could launch on its own: a `manual` card is waiting for a human who is evidently not there.
//
// Pausing auto-execution therefore releases the hold, unless something is already running: nothing
// can start while the switch is off, so there is nothing to stay awake for.
//
// **Deliberately not `ConcurrencySlots.Occupies`, close as the two look.** The cap counts work in
// flight and so counts an `error` or `killed` card, which is a failure the user still owes an
// answer to. Sleep is about processes, and those two have none — keeping a laptop up all night for
// a session that died at 3am protects nothing.
public static class SleepPolicy
{
    public static bool ShouldHold(IEnumerable<Card> cards, bool keepAwake, bool paused)
    {
        if (!keepAwake)
            return false;

        return cards.Any(card => card.IsOnBoard && (Live(card) || (!paused && Pending(card))));
    }

    private static bool Live(Card card)
        => card.Column is BoardColumn.Executing
            || (card.Column is BoardColumn.YourTurn
                && card.Badge is Badge.NeedsPermission or Badge.NeedsAnswer or Badge.Compacting);

    private static bool Pending(Card card)
        => card.Column is BoardColumn.Ready
            && card.Schedule is not (null or TaskSchedule.Manual);
}
