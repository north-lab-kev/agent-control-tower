using Act.Core.Model;

namespace Act.Core.Scheduling;

// The safety net between `schedule` (intent) and the quota (reality): a card whose agent has no
// budget left waits for the window to reset rather than launching into an error.
//
// Two rules make it honest. **A reading held past its own reset is not a limit** — ACT polls, so a
// window that has rolled over says nothing about the new one, and `HasRolledOver` is what stops a
// stale 100% locking the queue out until the next poll lands. And **no reading is not a limit
// either**: an unavailable probe (not signed in, token expired, no connection) must not silently
// freeze an overnight run, so the queue launches and lets the CLI be the one to refuse.
public static class UsageBackpressure
{
    // The window a card must wait for, or null when nothing is in the way. `ceilingPercent` is where
    // the user has set the quota to count as spent — see `UsageCeiling` for why that is not 100.
    public static UsageWindow? Blocking(AgentUsage? usage, DateTimeOffset now, int ceilingPercent)
    {
        if (usage is null)
            return null;

        // A window with no reset instant is one nothing has run in yet, and what this returns is an
        // instant to wait for — so it cannot be the answer even if it somehow reported exhaustion. The
        // queue then launches and lets the CLI refuse, which is the same choice as for no reading at all.
        var live = usage.Windows
            .Where(window => window.ResetsAt is not null && !window.HasRolledOver(now))
            .ToList();

        var ceiling = UsageCeiling.Clamp(ceilingPercent);

        // A window at or above the ceiling names itself, and the **latest** such reset is the one to
        // wait for: launching at the 5-hour boundary against a spent weekly quota would only fail
        // again, and the chip would be counting to the wrong instant.
        var spent = live.Where(window => window.Percent >= ceiling).ToList();

        if (spent.Count > 0)
            return spent.MaxBy(window => window.ResetsAt);

        // The account-level flag says a limit was hit without saying which, so the **soonest** reset
        // is the answer: it is the likeliest cause, and being wrong costs one re-check rather than
        // days of a queue held against a weekly boundary that was never the problem.
        return usage.LimitReached ? live.MinBy(window => window.ResetsAt) : null;
    }
}
