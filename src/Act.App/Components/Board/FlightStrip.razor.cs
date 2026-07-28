using System.Globalization;
using Act.App.Resources;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Board;

public partial class FlightStrip
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    [Parameter, EditorRequired]
    public Card Card { get; set; } = default!;

    [Parameter]
    public BoardDensity Density { get; set; }

    private bool IsCompact => Density is BoardDensity.Compact;

    private bool ShowDot => Card.Badge is not null || Card.Column is BoardColumn.Completed;

    private string? AttentionClass => Card.NeedsAttention
        ? Card.Badge is Badge.Error or Badge.Killed ? "attn err" : "attn"
        : null;

    private string StripClass => $"strip {RailClass} {AttentionClass}".TrimEnd();

    private string FullBadgeClass => $"badge {BadgeClass}".TrimEnd();

    private string RailClass => Card.Badge switch
    {
        Badge.Running or Badge.Compacting => "r-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "r-wait",
        Badge.Error or Badge.Killed => "r-err",
        Badge.Stale => "r-stale",
        Badge.Idle => "r-review",
        _ => Card.Column switch
        {
            BoardColumn.Ready => "r-ready",
            BoardColumn.Completed => "r-done",
            _ => "r-plan",
        },
    };

    private string? BadgeClass => Card.Badge switch
    {
        Badge.Running or Badge.Compacting => "b-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "b-wait",
        Badge.Error or Badge.Killed => "b-err",
        Badge.Stale => "b-stale",
        Badge.Idle => "b-review",
        _ => Card.Column is BoardColumn.Completed ? "b-done" : null,
    };

    private string? BadgeText => Card.Badge switch
    {
        Badge.Running => Strings.Badge_Running,
        Badge.Compacting => Strings.Badge_Compacting,
        Badge.NeedsPermission => Strings.Badge_NeedsPermission,
        Badge.NeedsAnswer => Strings.Badge_NeedsAnswer,
        Badge.Error => Strings.Badge_Error,
        Badge.Killed => Strings.Badge_Killed,
        Badge.Stale => StaleText,
        Badge.Idle => Strings.Badge_IdleReady,
        _ => Card.Column is BoardColumn.Completed ? Strings.Badge_Done : null,
    };

    private string StaleText
    {
        get
        {
            var since = Card.Metrics?.LastActivityAt;
            if (since is null)
                return Strings.Badge_Stale;

            var minutes = (int)(DateTimeOffset.UtcNow - since.Value).TotalMinutes;

            return minutes < 60
                ? Text.Format(Strings.Badge_StaleMinutes, minutes)
                : Text.Format(Strings.Badge_StaleHours, minutes / 60);
        }
    }

    private string AgentSuffix
    {
        get
        {
            var agent = Card.AgentType is "claude-code" ? "claude" : Card.AgentType;
            return Card.ObservedModel is null ? $" · {agent}" : $" · {agent} · {Card.ObservedModel}";
        }
    }

    private string? ScheduleText => Card.Schedule switch
    {
        TaskSchedule.Manual => Strings.Schedule_Manual,
        TaskSchedule.Now => Strings.Schedule_Now,
        TaskSchedule.NextWindow => Strings.Schedule_NextWindow,
        TaskSchedule.WindowAfterNext => Strings.Schedule_WindowAfterNext,
        TaskSchedule.SpecificDateTime => Text.Format(
            Strings.Schedule_At,
            Card.ScheduledFor?.ToString(Strings.Schedule_DateFormat, Culture)),
        _ => null,
    };

    private static IEnumerable<string> Metrics(CardMetrics m)
    {
        if (m.ContextPercent is not null)
            yield return Text.Format(Strings.Metrics_Context, Thousands(m.ContextUsed), Thousands(m.ContextLimit));

        if (m.TurnCount > 0)
            yield return Text.Format(Strings.Metrics_Turns, m.TurnCount);

        if (m.Compactions > 0)
            yield return Text.Format(Strings.Metrics_Compactions, m.Compactions);

        if (m.Cost > 0)
            yield return m.Cost.ToString("C2", Culture);
    }

    private static string Thousands(int value)
        => value >= 1000 ? $"{value / 1000}k" : value.ToString(Culture);
}
