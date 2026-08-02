using System.Globalization;
using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
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
    public bool Blink { get; set; } = true;

    [Parameter]
    public EventCallback<Card> OnOpen { get; set; }

    [Parameter]
    public EventCallback<Card> OnLaunch { get; set; }

    [Parameter]
    public EventCallback<Card> OnRetry { get; set; }

    // Whether this card can be retried, decided by the board — the answer depends on whether a
    // session is live, which is registry state the strip has no business reaching for.
    [Parameter]
    public bool Retriable { get; set; }

    [Parameter]
    public EventCallback<Card> OnDragStart { get; set; }

    [Parameter]
    public EventCallback OnDragEnd { get; set; }

    [Parameter]
    public bool IsDragging { get; set; }

    [Parameter]
    public bool IsLaunching { get; set; }

    // Why this Ready card is not running, decided by the board in one pass over every card — the
    // same pass the queue runner launches from, so a strip cannot claim a card is queued while it
    // is starting.
    [Parameter]
    public ReadyHold? Hold { get; set; }

    private bool Draggable => ManualMove.CanDrag(Card.Column);

    private bool IsCompact => Density is BoardDensity.Compact;

    private Task OpenAsync() => OnOpen.InvokeAsync(Card);

    private Task OnDragStartAsync() => Draggable ? OnDragStart.InvokeAsync(Card) : Task.CompletedTask;

    private Task OnDragEndAsync() => OnDragEnd.InvokeAsync();

    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => args.Key is "Enter" or " " ? OnOpen.InvokeAsync(Card) : Task.CompletedTask;

    // One blink, three colours. Ready for review pulses too — it is as much the user's turn as a
    // permission prompt is — but in the calm blue the review rail already uses, so a scan still
    // separates "something is stuck" from "something is done".
    private string? AttentionClass => Card.Badge switch
    {
        Badge.Error or Badge.Killed => "attn err",
        Badge.ReadyForReview => "attn rev",
        _ when Card.NeedsAttention => "attn",
        _ => null,
    };

    private string StripClass => string.Join(' ', new[]
    {
        "strip",
        RailClass,
        AttentionClass,
        AttentionClass is not null && !Blink ? "still" : null,
        Draggable ? "liftable" : null,
        IsDragging ? "lifted" : null,
    }.Where(part => !string.IsNullOrEmpty(part)));

    private string FullBadgeClass => $"badge {BadgeClass}".TrimEnd();

    private string RailClass => Card.Badge switch
    {
        Badge.Running or Badge.Compacting => "r-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "r-wait",
        Badge.Error or Badge.Killed => "r-err",
        Badge.ReadyForReview => "r-review",
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

    private bool CanRetry => Retriable && !IsCompact;

    private Task LaunchAsync() => OnLaunch.InvokeAsync(Card);

    private Task RetryAsync() => OnRetry.InvokeAsync(Card);

    // The shared mapping, plus the one thing that is about this surface rather than the signal: a
    // Completed card shows `done` where it carries no badge.
    private string? BadgeClass => CardVisuals.BadgeClass(Card.Badge)
        ?? (Card.Column is BoardColumn.Completed ? "b-done" : null);

    private string? BadgeText => Card.Badge switch
    {
        null => Card.Column is BoardColumn.Completed ? Strings.Badge_Done : null,
        _ => CardVisuals.BadgeText(Card.Badge),
    };

    // Stated beside `running`, never instead of it — the card has gone quiet, and what that means is
    // the user's to judge. Computed at render time from the stored stamp, so it needs no event and no
    // stored state; the board's refresh tick is what makes the number advance.
    private string? QuietText => QuietSession.QuietFor(Card, DateTimeOffset.UtcNow) is { } quiet
        ? quiet.TotalMinutes < 60
            ? Text.Format(Strings.Metrics_QuietMinutes, (int)quiet.TotalMinutes)
            : Text.Format(Strings.Metrics_QuietHours, (int)quiet.TotalHours)
        : null;

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

    // A hold is not a `Badge` — it is derived, it is never stored, and it must not blink or notify.
    // It borrows the badge *slot*, which a Ready card leaves empty, and the muted colour the quiet
    // chip established: nothing here is wrong, and every one of these clears on its own.
    private string? HoldText => Hold is not { } hold ? null : hold.Reason switch
    {
        LaunchHold.Paused => Strings.Hold_Paused,
        LaunchHold.AgentDisabled => Strings.Hold_AgentOff,
        LaunchHold.UsageLimit => Text.Format(Strings.Hold_Usage, Countdown(hold.Until)),
        LaunchHold.WorkingDir => Text.Format(Strings.Hold_Folder, hold.Blocker?.Number),
        LaunchHold.Dependency => Text.Format(Strings.Hold_Dependency, hold.Blocker?.Number),
        LaunchHold.Slot => Text.Format(Strings.Hold_Slot, hold.Used, hold.Cap),
        _ => null,
    };

    private string? HoldTip => Hold is not { } hold ? null : hold.Reason switch
    {
        LaunchHold.Paused => Strings.Hold_Paused_Tip,
        LaunchHold.AgentDisabled => Text.Format(Strings.Hold_AgentOff_Tip, AgentName),
        LaunchHold.UsageLimit => Text.Format(
            Strings.Hold_Usage_Tip,
            AgentName,
            hold.Until?.ToLocalTime().ToString(Strings.Schedule_DateFormat, Culture)),
        LaunchHold.WorkingDir => Text.Format(
            Strings.Hold_Folder_Tip, hold.Blocker?.Number, hold.Blocker?.Title),
        LaunchHold.Dependency => Text.Format(
            Strings.Hold_Dependency_Tip, hold.Blocker?.Number, hold.Blocker?.Title),
        LaunchHold.Slot => Text.Format(Strings.Hold_Slot_Tip, hold.Used, hold.Cap),
        _ => null,
    };

    // `paused` and `agent off` are the user's own doing rather than something in the way, so they
    // stay neutral; the rest carry the quiet tint that says "waiting on something".
    private string HoldClass => Hold?.Reason is LaunchHold.Paused or LaunchHold.AgentDisabled
        ? "badge"
        : "badge b-hold";

    private static string Countdown(DateTimeOffset? until)
    {
        var left = (until ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow;

        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        if (left.TotalHours >= 24)
            return Text.Format(Strings.Hold_Days, (int)left.TotalDays, left.Hours);

        return left.TotalMinutes >= 60
            ? Text.Format(Strings.Hold_Hours, (int)left.TotalHours, left.Minutes)
            : Text.Format(Strings.Hold_Minutes, (int)left.TotalMinutes);
    }

    private static IEnumerable<string> Metrics(CardMetrics m)
    {
        if (m.ContextPercent is not null)
            yield return Text.Format(
                Strings.Metrics_Context,
                Text.Thousands(m.ContextUsed),
                Text.Thousands(m.ContextLimit));

        if (m.TurnCount > 0)
            yield return Text.Format(Strings.Metrics_Turns, m.TurnCount);

        if (m.Compactions > 0)
            yield return Text.Format(Strings.Metrics_Compactions, m.Compactions);

        if (m.TokensTotal > 0)
            yield return Text.Format(Strings.Metrics_Tokens, Text.Thousands(m.TokensTotal));
    }
}
