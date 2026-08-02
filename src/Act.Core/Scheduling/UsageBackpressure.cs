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
    private const int ExhaustedPercent = 100;

    // The window a card must wait for, or null when nothing is in the way.
    public static UsageWindow? Blocking(AgentUsage? usage, DateTimeOffset now)
    {
        if (usage is null)
            return null;

        var live = usage.Windows.Where(window => !window.HasRolledOver(now)).ToList();

        // A window reporting its own exhaustion names itself, and the **latest** such reset is the
        // one to wait for: launching at the 5-hour boundary against a spent weekly quota would only
        // fail again, and the chip would be counting to the wrong instant.
        var spent = live.Where(window => window.Percent >= ExhaustedPercent).ToList();

        if (spent.Count > 0)
            return spent.MaxBy(window => window.ResetsAt);

        // The account-level flag says a limit was hit without saying which, so the **soonest** reset
        // is the answer: it is the likeliest cause, and being wrong costs one re-check rather than
        // days of a queue held against a weekly boundary that was never the problem.
        return usage.LimitReached ? live.MinBy(window => window.ResetsAt) : null;
    }
}
