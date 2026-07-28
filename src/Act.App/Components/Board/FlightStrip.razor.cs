using System.Globalization;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Board;

public partial class FlightStrip
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

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
        Badge.Running => "running",
        Badge.Compacting => "compacting",
        Badge.NeedsPermission => "needs permission",
        Badge.NeedsAnswer => "needs answer",
        Badge.Error => "error",
        Badge.Killed => "killed",
        Badge.Stale => StaleText,
        Badge.Idle => "idle · ready",
        _ => Card.Column is BoardColumn.Completed ? "done" : null,
    };

    private string StaleText
    {
        get
        {
            var since = Card.Metrics?.LastActivityAt;
            if (since is null)
            {
                return "stale";
            }

            var minutes = (int)(DateTimeOffset.UtcNow - since.Value).TotalMinutes;
            return minutes < 60 ? $"stale {minutes}m" : $"stale {minutes / 60}h";
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
        TaskSchedule.Manual => "manual",
        TaskSchedule.Now => "now",
        TaskSchedule.NextWindow => "next win",
        TaskSchedule.WindowAfterNext => "win +2",
        TaskSchedule.SpecificDateTime => $"⏱ {Card.ScheduledFor?.ToString("MMM d HH:mm", Culture)}",
        _ => null,
    };

    private static IEnumerable<string> Metrics(CardMetrics m)
    {
        if (m.ContextPercent is not null)
        {
            yield return $"{Thousands(m.ContextUsed)}/{Thousands(m.ContextLimit)} ctx";
        }

        if (m.TurnCount > 0)
        {
            yield return $"{m.TurnCount} turns";
        }

        if (m.Compactions > 0)
        {
            yield return $"{m.Compactions} compactions";
        }

        if (m.Cost > 0)
        {
            yield return m.Cost.ToString("C2", Culture);
        }
    }

    private static string Thousands(int value)
        => value >= 1000 ? $"{value / 1000}k" : value.ToString(Culture);
}
