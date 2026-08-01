using Act.Core.Model;

namespace Act.Infrastructure.Usage;

public sealed class UsageOptions
{
    public const string SectionName = "Usage";

    private const int ShortestPollSeconds = 60;

    private const int LongestPollSeconds = 3600;

    public bool Enabled { get; init; } = true;

    public int PollSeconds { get; init; } = 180;

    public Dictionary<string, UsageAgentOptions> Agents { get; init; } = [];

    public TimeSpan PollInterval
        => TimeSpan.FromSeconds(Math.Clamp(PollSeconds, ShortestPollSeconds, LongestPollSeconds));

    public UsageAgentOptions For(AgentType agent)
    {
        foreach (var pair in Agents)
        {
            if (string.Equals(pair.Key, agent.ToString(), StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return new UsageAgentOptions();
    }
}

public sealed class UsageAgentOptions
{
    public string? CredentialsPath { get; init; }

    public string? Endpoint { get; init; }
}
