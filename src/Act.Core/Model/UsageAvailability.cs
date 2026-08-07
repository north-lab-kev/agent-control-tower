namespace Act.Core.Model;

public enum UsageAvailability
{
    Available,
    Off,
    NotSignedIn,

    // Recoverable without the user: the access token's own clock ran out but the refresh token
    // beside it is still good, so running the CLI once renews it. `SignInRequired` is the same
    // situation after that second clock has run out too, and the difference is the whole point —
    // one resolves itself, the other cannot be resolved by any amount of waiting.
    Expired,
    SignInRequired,

    Unauthorized,
    Unreachable,
    Failed,
}

public enum UsageTokenState
{
    Present,
    Missing,
    Expired,
    Lapsed,
}

public sealed record UsageToken(UsageTokenState State, string? Value)
{
    public static readonly UsageToken Missing = new(UsageTokenState.Missing, null);

    public static readonly UsageToken Expired = new(UsageTokenState.Expired, null);

    public static readonly UsageToken Lapsed = new(UsageTokenState.Lapsed, null);

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
