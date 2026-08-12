using Act.Core.Model;

namespace Act.Core.Rules;

// How long a running card has had nothing to say. Deliberately **not** a state: ACT cannot tell a
// hung agent from a long build from its own ingestion having broken, so the card keeps saying
// `running` — which is still ACT's best knowledge — and this is rendered beside it as the plain
// fact it is. A badge here would claim something no signal supports, and would overwrite the one
// thing ACT does know.
//
// See the spec's *No stale badge — the quiet chip instead*.
public static class QuietSession
{
    // ~20× the p99 of real within-turn gaps: far outside normal chatter, still short enough to
    // notice overnight. `docs/design-notes.md` has the measurement it comes from.
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(15);

    // Null when there is nothing worth saying. **Executing only**: a card in Your turn is quiet
    // *because* it is waiting for the user, which is its resting state and not news, and a card
    // before the launch boundary has no session to be quiet.
    public static TimeSpan? QuietFor(Card card, DateTimeOffset now)
    {
        if (card.Column is not BoardColumn.Executing)
            return null;

        if (card.Metrics?.LastActivityAt is not { } last)
            return null;

        var quiet = now - last;

        return quiet >= Threshold ? quiet : null;
    }
}
