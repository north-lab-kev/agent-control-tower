using Act.Core.Model;

namespace Act.Core.Rules;

// Which failed cards can be run again, and — just as important — which ones ACT must not offer to.
//
// **Only when the process is gone.** A CLI that is still alive is one the user can type into, and
// the terminal is a click away; ACT types nothing into a live agent by rule, so "retry" there would
// have to mean killing a session that is still usable. The action exists for the case the user
// genuinely cannot do themselves: the process died and the work stopped with it.
//
// **Only with a session to resume.** Retry continues a run; it does not start one. A card that
// errored before it ever bound a session has nothing to continue, and starting from scratch is what
// duplicating the task is for — which is why there is no fresh-seed fallback here.
public static class CardRetry
{
    public static bool CanRetry(Card card, bool sessionLive)
        => !card.IsDeleted
            && card.Column is BoardColumn.YourTurn
            && card.Badge is Badge.Error
            && card.SessionId is { Length: > 0 }
            && !sessionLive;
}
