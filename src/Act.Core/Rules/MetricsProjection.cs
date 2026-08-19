using Act.Core.Events;
using Act.Core.Model;

namespace Act.Core.Rules;

// What the observed stream does to a card's numbers, kept separate from what it does to its column:
// metrics accumulate on every event and never route anything, so they are safe to apply first and
// unconditionally. Pure, so the whole projection is unit-testable with no session anywhere.
public static class MetricsProjection
{
    public static bool Apply(Card card, AgentEvent observed)
    {
        var metrics = card.Metrics ??= new CardMetrics();

        switch (observed)
        {
            case ActivityObserved activity:
                Touch(metrics, activity.At);

                // Only a named tool is a tool call. `UserPromptSubmit` arrives as activity too, and
                // counting it would inflate the number the strip shows.
                if (activity.ToolName is { Length: > 0 })
                    metrics.ToolCalls++;

                return true;

            // A failed turn is still a turn: it was attempted, it is over, and hiding it from the count
            // would make a card that failed four times look untouched. An interrupted one counts for
            // the same reason — the work it did before the keystroke is on the screen and up for
            // review.
            case TurnEnded or TurnFailed or TurnInterrupted:
                metrics.TurnCount++;

                Touch(metrics, observed.At);

                return true;

            case CompactingStarted compacting:
                metrics.Compactions++;

                Touch(metrics, compacting.At);

                return true;

            case SessionStarted or CompactingFinished or PermissionRequested or QuestionAsked:
                Touch(metrics, observed.At);

                return true;

            case SessionEnriched enriched:
                return Merge(card, metrics, enriched);

            default:
                return false;
        }
    }

    // Null means "no news", not zero — a transcript line may carry only a token count, so every
    // field is merged independently and an absent one leaves what was already known alone.
    private static bool Merge(Card card, CardMetrics metrics, SessionEnriched enriched)
    {
        var snapshot = enriched.Snapshot;

        if (snapshot.ObservedModel is { Length: > 0 } model)
            card.ObservedModel = model;

        if (snapshot.TokensIn is { } tokensIn)
            metrics.TokensIn = tokensIn;

        if (snapshot.TokensOut is { } tokensOut)
            metrics.TokensOut = tokensOut;

        if (snapshot.ContextUsed is { } used)
            metrics.ContextUsed = used;

        if (snapshot.ContextLimit is { } limit)
            metrics.ContextLimit = limit;

        if (snapshot.TurnCount is { } turns)
            metrics.TurnCount = turns;

        if (snapshot.ToolCalls is { } calls)
            metrics.ToolCalls = calls;

        Touch(metrics, enriched.At);

        return true;
    }

    // Monotonic, never a plain assignment. The stamp on an observed event is the *source's* clock —
    // a transcript line carries its own, and a line ACT cannot read one from falls back to a
    // sentinel — so an assignment lets "last activity" jump backwards, and everything derived from
    // it (the quiet chip above all) then describes a session that has not gone quiet at all.
    private static void Touch(CardMetrics metrics, DateTimeOffset at)
    {
        if (metrics.LastActivityAt is not { } last || at > last)
            metrics.LastActivityAt = at;
    }
}
