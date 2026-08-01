namespace Act.Core.Model;

public enum UsageAvailability
{
    Available,
    Off,
    NotSignedIn,
    Expired,
    Unauthorized,
    Unreachable,
    Failed,
}

public enum UsageTokenState
{
    Present,
    Missing,
    Expired,
}

public sealed record UsageToken(UsageTokenState State, string? Value)
{
    public static readonly UsageToken Missing = new(UsageTokenState.Missing, null);

    public static readonly UsageToken Expired = new(UsageTokenState.Expired, null);

    public static UsageToken Present(string value) => new(UsageTokenState.Present, value);
}

public sealed record UsageProbeResult(
    AgentType Agent,
    UsageAvailability Availability,
    AgentUsage? Usage,
    DateTimeOffset At)
{
    public static UsageProbeResult Of(AgentUsage usage)
        => new(usage.Agent, UsageAvailability.Available, usage, usage.TakenAt);

    public static UsageProbeResult Unavailable(AgentType agent, UsageAvailability availability, DateTimeOffset at)
        => new(agent, availability, null, at);

    public bool IsAvailable => Availability is UsageAvailability.Available && Usage is not null;

    public bool IsUnavailable => Availability is not (UsageAvailability.Available or UsageAvailability.Off);
}
