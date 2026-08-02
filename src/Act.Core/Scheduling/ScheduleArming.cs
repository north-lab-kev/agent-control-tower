using Act.Core.Model;

namespace Act.Core.Scheduling;

// When a Ready card is allowed to start, as one instant.
//
// `Manual` never resolves and `Now` resolves to the moment it is asked, so neither is stored. The
// two window schedules are the reason this exists: "the next window" means the boundary that was
// next **when the card was armed**, and a value recomputed from live usage would keep sliding
// forward — the moment that boundary passes, the reading names the one after it, and the card would
// be perpetually one window away from starting. So they are resolved once against the reading of
// the day and written to `card.EligibleAt`, which also makes the queue survive a restart.
public static class ScheduleArming
{
    public static bool NeedsArming(Card card)
        => card.Column is BoardColumn.Ready
            && card.EligibleAt is null
            && card.Schedule is TaskSchedule.NextWindow or TaskSchedule.WindowAfterNext;

    // Null when there is no reading to arm against. The card then waits — unlike backpressure, where
    // an unreadable quota launches anyway, a window boundary ACT cannot see is not something to
    // guess at, and the intent chip already says the card is waiting for one.
    public static DateTimeOffset? Arm(Card card, UsageWindow? session)
    {
        if (!NeedsArming(card) || session is null)
            return null;

        return card.Schedule is TaskSchedule.WindowAfterNext
            ? session.ResetsAt + session.Duration
            : session.ResetsAt;
    }

    // The instant this card is due, or null when it is due as soon as everything else allows.
    // `Manual` is not answered here at all — it is not scheduling, it is the absence of it.
    public static DateTimeOffset? DueAt(Card card) => card.Schedule switch
    {
        TaskSchedule.SpecificDateTime => card.ScheduledFor,
        TaskSchedule.NextWindow or TaskSchedule.WindowAfterNext => card.EligibleAt,
        _ => null,
    };

    public static bool IsDue(Card card, DateTimeOffset now) => card.Schedule switch
    {
        TaskSchedule.Now => true,
        TaskSchedule.SpecificDateTime => card.ScheduledFor is { } at && now >= at,
        TaskSchedule.NextWindow or TaskSchedule.WindowAfterNext => card.EligibleAt is { } at && now >= at,
        _ => false,
    };
}
