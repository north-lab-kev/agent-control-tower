using System.Globalization;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.Components.Board;

public partial class FlightStrip
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    [Parameter, EditorRequired]
    public Card Card { get; set; } = default!;

    [Parameter]
    public BoardDensity Density { get; set; }

    [Parameter]
    public EventCallback<Card> OnOpen { get; set; }

    [Parameter]
    public EventCallback<Card> OnLaunch { get; set; }

    [Parameter]
    public EventCallback<Card> OnDragStart { get; set; }

    [Parameter]
    public EventCallback OnDragEnd { get; set; }

    [Parameter]
    public bool IsDragging { get; set; }

    private bool Draggable => ManualMove.CanDrag(Card.Column);

    private bool IsCompact => Density is BoardDensity.Compact;

    private Task OpenAsync() => OnOpen.InvokeAsync(Card);

    private Task OnDragStartAsync() => Draggable ? OnDragStart.InvokeAsync(Card) : Task.CompletedTask;

    private Task OnDragEndAsync() => OnDragEnd.InvokeAsync();

    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => args.Key is "Enter" or " " ? OnOpen.InvokeAsync(Card) : Task.CompletedTask;

    private string? AttentionClass => Card.NeedsAttention
        ? Card.Badge is Badge.Error or Badge.Killed ? "attn err" : "attn"
        : null;

    private string StripClass => string.Join(' ', new[]
    {
        "strip",
        RailClass,
        AttentionClass,
        Draggable ? "liftable" : null,
        IsDragging ? "lifted" : null,
    }.Where(part => !string.IsNullOrEmpty(part)));

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

    // Spacious only: the compact strip has no room for it without pushing the badge out, and the
    // badge is the signal the density exists to preserve.
    private bool CanLaunch => Card.Column is BoardColumn.Ready && !IsCompact;

    private Task LaunchAsync() => OnLaunch.InvokeAsync(Card);

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

    private string AgentName => Card.AgentType switch
    {
        AgentType.ClaudeCode => Strings.Agent_ClaudeCode,
        AgentType.Codex => Strings.Agent_Codex,
        _ => Card.AgentType.ToString(),
    };

    private string AgentSuffix => Card.ObservedModel is null
        ? $" · {AgentName}"
        : $" · {AgentName} · {Card.ObservedModel}";

    private string? ScheduleText => Card.Column is not BoardColumn.Ready ? null : Card.Schedule switch
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
