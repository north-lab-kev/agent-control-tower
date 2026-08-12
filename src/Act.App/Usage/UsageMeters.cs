using System.Globalization;
using Act.App.Resources;
using Act.Core.Model;

namespace Act.App.Usage;

public sealed record UsageMeter(
    string Label,
    string Value,
    string Reset,
    string Tooltip,
    double Percent,
    string State);

public sealed record UsageNotice(string Label, string Reason, string Tooltip);

// What the top bar says about each agent's quota, as a function of the readings, the agents that are
// switched on, and the instant it is drawn at. Split out of `UsageIndicator` because all of it is
// pure and none of it was reachable from a test: the percentage a rolled-over window reports, which
// pressure tint a reading earns, and how a remaining span is worded are three things worth being
// sure of, and the bar is the only place ACT states a number it did not compute itself.
public static class UsageMeters
{
    // Only the agents you are actually using. A disabled agent cannot be given a task, so its quota
    // is not a number you can act on — and the top bar is the one place with no room for information
    // that leads nowhere.
    public static IReadOnlyList<UsageMeter> For(
        IEnumerable<UsageProbeResult> results,
        IReadOnlyCollection<AgentType> enabled,
        DateTimeOffset now)
        =>
        [
            .. results
                .Where(result => result.IsAvailable && enabled.Contains(result.Agent))
                .Select(result => result.Usage!)
                .SelectMany(reading => reading.Windows
                    .OrderBy(window => window.Kind)
                    .Select(window => Describe(reading, window, now))),
        ];

    public static IReadOnlyList<UsageNotice> Notices(
        IEnumerable<UsageProbeResult> results,
        IReadOnlyCollection<AgentType> enabled)
        =>
        [
            .. results
                .Where(result => result.IsUnavailable && enabled.Contains(result.Agent))
                .Select(Explain),
        ];

    public static string Name(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => Strings.Usage_Agent_ClaudeCode,
        AgentType.Codex => Strings.Usage_Agent_Codex,
        _ => agent.ToString(),
    };

    // A window is named by the length the server declared, never by a fixed caption pair — a free
    // Codex plan reports a single 30-day window and hardcoding two bars would mislabel it.
    public static string Name(UsageWindowKind kind) => kind switch
    {
        UsageWindowKind.Session => Strings.Usage_Window_Session,
        UsageWindowKind.Weekly => Strings.Usage_Window_Weekly,
        UsageWindowKind.Monthly => Strings.Usage_Window_Monthly,
        _ => string.Empty,
    };

    private static UsageMeter Describe(AgentUsage reading, UsageWindow window, DateTimeOffset now)
    {
        var rolled = window.HasRolledOver(now);

        // A reading held past its own reset reports zero, not the number it was given: ACT polls, so
        // nothing ran in between and the stale percentage describes a window that no longer exists.
        var percent = window.PercentAt(now);

        // Three cases, not two: a window can also have no reset at all, which is a 5-hour window nothing
        // has run in yet. It reads as *not started* rather than as a countdown, and it is still drawn —
        // dropping the meter for a missing instant read as "ACT cannot see your session quota".
        var reset = rolled
            ? Strings.Usage_AfterReset
            : window.RemainingAt(now) is { } left
                ? Format(Strings.Usage_ResetsIn, Left(left))
                : Strings.Usage_NotStarted;

        var tooltip = window.ResetsAt is { } at
            ? Format(
                Strings.Usage_Tooltip,
                Name(reading.Agent),
                Name(window.Kind),
                at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Format(Strings.Usage_Tooltip_NotStarted, Name(reading.Agent), Name(window.Kind));

        return new UsageMeter(
            $"{Name(reading.Agent)} {Name(window.Kind)}",
            Format(Strings.Usage_Percent, percent),
            reset,
            tooltip,
            percent,
            State(window.PressureAt(now, reading.LimitReached)));
    }

    private static UsageNotice Explain(UsageProbeResult result)
    {
        var at = result.At.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

        return new UsageNotice(
            Name(result.Agent),
            Reason(result.Availability),
            Format(Strings.Usage_Unavailable_Tooltip, Name(result.Agent), Cause(result.Availability), at));
    }

    private static string State(UsagePressure pressure) => pressure switch
    {
        UsagePressure.Elevated => "mid",
        UsagePressure.Critical => "high",
        UsagePressure.RolledOver => "rolled",
        _ => "low",
    };

    private static string Reason(UsageAvailability availability) => availability switch
    {
        UsageAvailability.NotSignedIn => Strings.Usage_Unavailable_NotSignedIn,
        UsageAvailability.Expired => Strings.Usage_Unavailable_Expired,
        UsageAvailability.SignInRequired => Strings.Usage_Unavailable_SignInRequired,
        UsageAvailability.Unauthorized => Strings.Usage_Unavailable_Unauthorized,
        UsageAvailability.Unreachable => Strings.Usage_Unavailable_Unreachable,
        _ => Strings.Usage_Unavailable_Failed,
    };

    private static string Cause(UsageAvailability availability) => availability switch
    {
        UsageAvailability.NotSignedIn => Strings.Usage_Cause_NotSignedIn,
        UsageAvailability.Expired => Strings.Usage_Cause_Expired,
        UsageAvailability.SignInRequired => Strings.Usage_Cause_SignInRequired,
        UsageAvailability.Unauthorized => Strings.Usage_Cause_Unauthorized,
        UsageAvailability.Unreachable => Strings.Usage_Cause_Unreachable,
        _ => Strings.Usage_Cause_Failed,
    };

    // Coarsening on purpose: the question a quota answers is "how long have I got", and a countdown
    // to the second would redraw every tick to say nothing new.
    private static string Left(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
            return Format(Strings.Usage_DaysHours, (int)remaining.TotalDays, remaining.Hours);

        if (remaining.TotalHours >= 1)
            return Format(Strings.Usage_HoursMinutes, (int)remaining.TotalHours, remaining.Minutes);

        // Never "0 minutes": a window that is nearly over still has something left, and rounding it
        // away would read as though it had already reset.
        return Format(Strings.Usage_Minutes, Math.Max(1, (int)remaining.TotalMinutes));
    }

    private static string Format(string format, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, format, values);
}
