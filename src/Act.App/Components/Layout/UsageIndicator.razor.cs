using System.Globalization;
using Act.App.Resources;
using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Components.Layout;

public partial class UsageIndicator(UsageState usage, UserSettingsService settings, IClock clock) : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource stopping = new();

    private IReadOnlyList<Meter> Meters => Build();

    private IReadOnlyList<Notice> Notices => BuildNotices();

    protected override void OnInitialized()
    {
        usage.Changed += OnChanged;

        // A meter for an agent you have switched off has to leave the bar the moment you switch it,
        // not whenever the next poll happens to land.
        settings.Changed += OnChanged;

        _ = TickAsync();
    }

    public void Dispose()
    {
        usage.Changed -= OnChanged;
        settings.Changed -= OnChanged;

        stopping.Cancel();
        stopping.Dispose();
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(Tick);

        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
                await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Only the agents you are actually using. A disabled agent cannot be given a task, so its
    // quota is not a number you can act on — and the top bar is the one place with no room for
    // information that leads nowhere.
    private IReadOnlyList<Meter> Build()
    {
        var now = clock.Now;
        var enabled = settings.EnabledAgents;

        return
        [
            .. usage.Results
                .Where(result => result.IsAvailable && enabled.Contains(result.Agent))
                .Select(result => result.Usage!)
                .SelectMany(reading => reading.Windows
                    .OrderBy(window => window.Kind)
                    .Select(window => Describe(reading, window, now))),
        ];
    }

    private IReadOnlyList<Notice> BuildNotices()
    {
        var enabled = settings.EnabledAgents;

        return
        [
            .. usage.Results
                .Where(result => result.IsUnavailable && enabled.Contains(result.Agent))
                .Select(Explain),
        ];
    }

    private static Notice Explain(UsageProbeResult result)
    {
        var at = result.At.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

        return new Notice(
            Name(result.Agent),
            Reason(result.Availability),
            Text(Strings.Usage_Unavailable_Tooltip, Name(result.Agent), Cause(result.Availability), at));
    }

    private static string Reason(UsageAvailability availability) => availability switch
    {
        UsageAvailability.NotSignedIn => Strings.Usage_Unavailable_NotSignedIn,
        UsageAvailability.Expired => Strings.Usage_Unavailable_Expired,
        UsageAvailability.Unauthorized => Strings.Usage_Unavailable_Unauthorized,
        UsageAvailability.Unreachable => Strings.Usage_Unavailable_Unreachable,
        _ => Strings.Usage_Unavailable_Failed,
    };

    private static string Cause(UsageAvailability availability) => availability switch
    {
        UsageAvailability.NotSignedIn => Strings.Usage_Cause_NotSignedIn,
        UsageAvailability.Expired => Strings.Usage_Cause_Expired,
        UsageAvailability.Unauthorized => Strings.Usage_Cause_Unauthorized,
        UsageAvailability.Unreachable => Strings.Usage_Cause_Unreachable,
        _ => Strings.Usage_Cause_Failed,
    };

    private static Meter Describe(AgentUsage reading, UsageWindow window, DateTimeOffset now)
    {
        var rolled = window.HasRolledOver(now);
        var percent = window.PercentAt(now);

        var reset = rolled
            ? Strings.Usage_AfterReset
            : Text(Strings.Usage_ResetsIn, Left(window.RemainingAt(now)));

        var tooltip = Text(
            Strings.Usage_Tooltip,
            Name(reading.Agent),
            Name(window.Kind),
            window.ResetsAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            reading.TakenAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));

        return new Meter(
            $"{Name(reading.Agent)} {Name(window.Kind)}",
            Text(Strings.Usage_Percent, percent),
            reset,
            tooltip,
            percent,
            State(window.PressureAt(now, reading.LimitReached)));
    }

    private static string State(UsagePressure pressure) => pressure switch
    {
        UsagePressure.Elevated => "mid",
        UsagePressure.Critical => "high",
        UsagePressure.RolledOver => "rolled",
        _ => "low",
    };

    private static string Left(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
            return Text(Strings.Usage_DaysHours, (int)remaining.TotalDays, remaining.Hours);

        if (remaining.TotalHours >= 1)
            return Text(Strings.Usage_HoursMinutes, (int)remaining.TotalHours, remaining.Minutes);

        return Text(Strings.Usage_Minutes, Math.Max(1, (int)remaining.TotalMinutes));
    }

    private static string Name(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => Strings.Usage_Agent_ClaudeCode,
        AgentType.Codex => Strings.Usage_Agent_Codex,
        _ => agent.ToString(),
    };

    private static string Name(UsageWindowKind kind) => kind switch
    {
        UsageWindowKind.Session => Strings.Usage_Window_Session,
        UsageWindowKind.Weekly => Strings.Usage_Window_Weekly,
        UsageWindowKind.Monthly => Strings.Usage_Window_Monthly,
        _ => string.Empty,
    };

    private static string Text(string format, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, format, values);

    private sealed record Meter(
        string Label,
        string Value,
        string Reset,
        string Tooltip,
        double Percent,
        string State);

    private sealed record Notice(string Label, string Reason, string Tooltip);
}
