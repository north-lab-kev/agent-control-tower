using System.Globalization;
using Act.App.Resources;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Components.Layout;

public partial class UsageIndicator(UsageState usage, IClock clock) : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource stopping = new();

    private IReadOnlyList<Meter> Meters => Build();

    protected override void OnInitialized()
    {
        usage.Changed += OnChanged;

        _ = TickAsync();
    }

    public void Dispose()
    {
        usage.Changed -= OnChanged;

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

    private IReadOnlyList<Meter> Build()
    {
        var now = clock.Now;

        return
        [
            .. usage.Readings.SelectMany(reading => reading.Windows
                .OrderBy(window => window.Kind)
                .Select(window => Describe(reading, window, now))),
        ];
    }

    private static Meter Describe(AgentUsage reading, UsageWindow window, DateTimeOffset now)
    {
        var rolled = window.HasRolledOver(now);
        var percent = window.PercentAt(now);

        var reset = rolled
            ? Strings.Usage_AfterReset
            : Text(Strings.Usage_Resets, Left(window.RemainingAt(now)), Stamp(window.ResetsAt, now));

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

    // A reset later today needs only a clock time; anything past midnight is ambiguous without the
    // day, and a monthly window is four weeks out. The day pattern is taken from the culture and
    // abbreviated in place, so the month/day order stays whatever that culture writes.
    private static string Stamp(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = resetsAt.ToLocalTime();
        var time = local.ToString("t", culture);

        if (local.Date == now.ToLocalTime().Date)
            return time;

        var day = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

        return Text(Strings.Usage_ResetStamp, local.ToString(day, culture), time);
    }

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
}
