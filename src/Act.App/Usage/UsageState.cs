using Act.Core.Model;

namespace Act.App.Usage;

public sealed class UsageState
{
    private readonly Dictionary<AgentType, UsageProbeResult> results = [];

    private readonly object gate = new();

    public event Action? Changed;

    public IReadOnlyList<UsageProbeResult> Results
    {
        get
        {
            lock (gate)
                return [.. results.Values.OrderBy(result => result.Agent)];
        }
    }

    // `Off` is the only outcome that removes rather than records, and the only one that can be
    // published over and over with nothing to say — an agent switched off is reported on every pass
    // of the poll loop. So it announces only when it actually took something away; anything else is
    // news by definition, since a reading carries the instant it was taken.
    public void Publish(UsageProbeResult result)
    {
        bool changed;

        lock (gate)
        {
            if (result.Availability is UsageAvailability.Off)
            {
                changed = results.Remove(result.Agent);
            }
            else
            {
                results[result.Agent] = result;
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke();
    }
}
