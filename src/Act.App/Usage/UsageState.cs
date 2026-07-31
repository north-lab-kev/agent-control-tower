using Act.Core.Model;

namespace Act.App.Usage;

public sealed class UsageState
{
    private readonly Dictionary<AgentType, AgentUsage> readings = [];

    private readonly object gate = new();

    public event Action? Changed;

    public IReadOnlyList<AgentUsage> Readings
    {
        get
        {
            lock (gate)
                return [.. readings.Values.OrderBy(reading => reading.Agent)];
        }
    }

    public void Publish(AgentType agent, AgentUsage? usage)
    {
        lock (gate)
        {
            if (usage is null)
                readings.Remove(agent);
            else
                readings[agent] = usage;
        }

        Changed?.Invoke();
    }
}
