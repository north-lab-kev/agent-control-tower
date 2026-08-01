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

    public void Publish(UsageProbeResult result)
    {
        lock (gate)
        {
            if (result.Availability is UsageAvailability.Off)
                results.Remove(result.Agent);
            else
                results[result.Agent] = result;
        }

        Changed?.Invoke();
    }
}
