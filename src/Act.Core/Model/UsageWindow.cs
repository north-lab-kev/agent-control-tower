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

public sealed record UsageWindow(
    UsageWindowKind Kind,
    int Percent,
    DateTimeOffset ResetsAt)
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

    public bool HasRolledOver(DateTimeOffset now) => now >= ResetsAt;

    public int PercentAt(DateTimeOffset now) => HasRolledOver(now) ? 0 : Percent;

    public TimeSpan RemainingAt(DateTimeOffset now)
    {
        var left = ResetsAt - now;

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
