namespace Act.Core.Model;

public enum UsageWindowKind
{
    Session,
    Weekly,
    Monthly,
}

public enum UsagePressure
{
    Normal,
    Elevated,
    Critical,
    RolledOver,
}

// `ResetsAt` is null for a window whose clock has not started: a 5-hour window with no activity in it
// reports a percentage and no reset, and dropping it for the missing instant is what once left the top
// bar showing a weekly meter and no session one. See `docs/findings/agent-usage.md`.
public sealed record UsageWindow(
    UsageWindowKind Kind,
    int Percent,
    DateTimeOffset? ResetsAt)
{
    private const int CriticalPercent = 90;

    private const int ElevatedPercent = 75;

    private static readonly TimeSpan LongestSession = TimeSpan.FromHours(8);

    private static readonly TimeSpan LongestWeek = TimeSpan.FromDays(8);

    public static UsageWindowKind Classify(TimeSpan length)
    {
        if (length <= LongestSession)
            return UsageWindowKind.Session;

        if (length <= LongestWeek)
            return UsageWindowKind.Weekly;

        return UsageWindowKind.Monthly;
    }

    public bool HasRolledOver(DateTimeOffset now) => ResetsAt is { } at && now >= at;

    public int PercentAt(DateTimeOffset now) => HasRolledOver(now) ? 0 : Percent;

    // Null when there is no reset to count down to, which is not the same answer as zero: one window
    // is about to turn over and the other has not begun.
    public TimeSpan? RemainingAt(DateTimeOffset now)
    {
        if (ResetsAt is not { } at)
            return null;

        var left = at - now;

        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public UsagePressure PressureAt(DateTimeOffset now, bool limitReached)
    {
        if (HasRolledOver(now))
            return UsagePressure.RolledOver;

        if (limitReached || Percent >= CriticalPercent)
            return UsagePressure.Critical;

        return Percent >= ElevatedPercent ? UsagePressure.Elevated : UsagePressure.Normal;
    }
}

public sealed record AgentUsage(
    AgentType Agent,
    IReadOnlyList<UsageWindow> Windows,
    DateTimeOffset TakenAt,
    bool LimitReached,
    string? Plan)
{
    public UsageWindow? Of(UsageWindowKind kind) => Windows.FirstOrDefault(window => window.Kind == kind);
}
