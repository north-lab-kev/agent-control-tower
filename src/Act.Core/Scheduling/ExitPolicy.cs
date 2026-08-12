using Act.Core.Model;

namespace Act.Core.Scheduling;

public enum ExitWarning
{
    None,
    Running,
    Scheduled,
    RunningAndScheduled,
}

public sealed record ExitStakes(int Running, bool Scheduled)
{
    public ExitWarning Warning => (Running > 0, Scheduled) switch
    {
        (true, true) => ExitWarning.RunningAndScheduled,
        (true, false) => ExitWarning.Running,
        (false, true) => ExitWarning.Scheduled,
        _ => ExitWarning.None,
    };
}

public static class ExitPolicy
{
    public static ExitStakes Assess(IEnumerable<Card> cards, bool paused)
    {
        var running = 0;
        var scheduled = false;

        foreach (var card in cards.Where(card => card.IsOnBoard))
        {
            if (Stopping(card))
                running++;
            else if (!paused && Pending(card))
                scheduled = true;
        }

        return new ExitStakes(running, scheduled);
    }

    private static bool Stopping(Card card) => card.Column switch
    {
        BoardColumn.Executing => true,
        BoardColumn.YourTurn => card.Badge is Badge.NeedsPermission,
        _ => false,
    };

    private static bool Pending(Card card)
        => card.Column is BoardColumn.Ready && card.Schedule is not (null or TaskSchedule.Manual);
}
