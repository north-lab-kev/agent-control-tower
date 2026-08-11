using System.Globalization;
using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;

namespace Act.App.Cards;

// Everything one flight strip says, as a function of the card and the instant it is drawn at. Split
// out of `FlightStrip` because none of it needs a rendered component — it is `Card` plus `ReadyHold`
// plus `now` in, strings out — and because it is the board's whole vocabulary: the badge, the rail
// colour, the blink, the hold chip and the quiet chip all decide here what a glance at the board
// tells you.
//
// The mapping shared with the other surfaces stays in `CardVisuals`. What lives here is what is
// about *this* surface rather than about the signal: a Completed card showing `done` where it
// carries no badge, the quiet gap stated beside `running` rather than instead of it, and the hold
// chip borrowing the badge slot a Ready card leaves empty.
public sealed record StripFace(
    string Identity,
    string? BadgeText,
    string? BadgeClass,
    string RailClass,
    string? AttentionClass,
    string? Quiet,
    string? Schedule,
    string? HoldText,
    string? HoldTip,
    string HoldClass,
    string? Spawned,
    string? SpawnedTip,
    IReadOnlyList<string> Metrics)
{
    public string FullBadgeClass => $"badge {BadgeClass}".TrimEnd();

    public static StripFace Of(
        Card card,
        ReadyHold? hold,
        DateTimeOffset now,
        Card? parent = null,
        AgentCapabilities? capabilities = null)
    {
        var agent = TaskLabels.Agent(card.AgentType);

        return new StripFace(
            Identity: Identify(card, agent, capabilities),
            BadgeText: BadgeTextFor(card),
            BadgeClass: BadgeClassFor(card),
            RailClass: RailClassFor(card),
            AttentionClass: AttentionClassFor(card),
            Quiet: QuietFor(card, now),
            Schedule: ScheduleFor(card),
            HoldText: HoldTextFor(hold, now),
            HoldTip: HoldTipFor(hold, agent),
            HoldClass: HoldClassFor(hold),
            Spawned: SpawnedFor(card, parent),
            SpawnedTip: SpawnedTipFor(card, parent),
            Metrics: MetricsFor(card.Metrics));
    }

    // The class the strip's own element carries. Composed here rather than in the markup so the one
    // place that decides whether a card blinks is also the place that decides what colour it is.
    public string Strip(bool blink, bool draggable, bool lifted, string? dropEdge)
        => string.Join(' ', new[]
        {
            "strip",
            RailClass,
            AttentionClass,
            AttentionClass is not null && !blink ? "still" : null,
            draggable ? "liftable" : null,
            lifted ? "lifted" : null,
            dropEdge,
        }.Where(part => !string.IsNullOrEmpty(part)));

    // The model is named the way the task form names it — `sonnet`, not the `claude-sonnet-5` the
    // transcript reports — so the strip and the dropdown that chose it read as one vocabulary. An id
    // no capability list claims is shown verbatim: a real id the user can look up beats nothing.
    private static string Identify(Card card, string agent, AgentCapabilities? capabilities)
        => (capabilities?.ModelFor(card.ObservedModel)?.Slug ?? card.ObservedModel) is { } model
            ? $"#{card.Number} · {agent} · {model}"
            : $"#{card.Number} · {agent}";

    // One blink, three colours. Ready for review pulses too — it is as much the user's turn as a
    // permission prompt is — but in the calm blue the review rail already uses, so a scan still
    // separates "something is stuck" from "something is done".
    private static string? AttentionClassFor(Card card) => card.Badge switch
    {
        Badge.Error or Badge.Killed => "attn err",
        Badge.ReadyForReview => "attn rev",
        _ when card.NeedsAttention => "attn",
        _ => null,
    };

    private static string RailClassFor(Card card) => card.Badge switch
    {
        Badge.Running or Badge.Compacting => "r-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "r-wait",
        Badge.Error or Badge.Killed => "r-err",
        Badge.ReadyForReview => "r-review",
        _ => card.Column switch
        {
            BoardColumn.Ready => "r-ready",
            BoardColumn.Completed => "r-done",
            _ => "r-plan",
        },
    };

    private static string? BadgeClassFor(Card card) => CardVisuals.BadgeClass(card.Badge)
        ?? (card.Column is BoardColumn.Completed ? "b-done" : null);

    private static string? BadgeTextFor(Card card) => card.Badge switch
    {
        null => card.Column is BoardColumn.Completed ? Strings.Badge_Done : null,
        _ => CardVisuals.BadgeText(card.Badge),
    };

    // Stated beside `running`, never instead of it — the card has gone quiet, and what that means is
    // the user's to judge.
    private static string? QuietFor(Card card, DateTimeOffset now)
        => QuietSession.QuietFor(card, now) is { } quiet
            ? quiet.TotalMinutes < 60
                ? Text.Format(Strings.Metrics_QuietMinutes, (int)quiet.TotalMinutes)
                : Text.Format(Strings.Metrics_QuietHours, (int)quiet.TotalHours)
            : null;

    private static string? ScheduleFor(Card card)
        => card.Column is not BoardColumn.Ready ? null : card.Schedule switch
        {
            TaskSchedule.Manual => Strings.Schedule_Manual,
            TaskSchedule.Now => Strings.Schedule_Now,
            TaskSchedule.NextWindow => Strings.Schedule_NextWindow,
            TaskSchedule.SpecificDateTime => Text.Format(
                Strings.Schedule_At,
                card.ScheduledFor?.ToString(Strings.Schedule_DateFormat, CultureInfo.CurrentCulture)),
            _ => null,
        };

    // A card an agent created rather than the user. Both densities carry it, because telling those
    // apart at a glance is the whole reason `parentId` is recorded — and it is a glyph plus a number
    // rather than a chip precisely so compact can afford it beside the path.
    //
    // A parent the board can no longer see (archived, purged) still marks the card as spawned: the
    // origin is a fact about how it came to exist, and dropping the marker would quietly relabel it
    // as something the user wrote.
    private static string? SpawnedFor(Card card, Card? parent)
        => card.Origin is not TaskOrigin.Spawned
            ? null
            : parent is { } known
                ? Text.Format(Strings.Card_SpawnedBy, known.Number)
                : Strings.Card_SpawnedByUnknown;

    private static string? SpawnedTipFor(Card card, Card? parent)
        => card.Origin is not TaskOrigin.Spawned
            ? null
            : parent is { } known
                ? Text.Format(Strings.Card_SpawnedBy_Tip, known.Number, known.Title)
                : Strings.Card_SpawnedByUnknown;

    // A hold is not a `Badge` — it is derived, it is never stored, and it must not blink or notify.
    private static string? HoldTextFor(ReadyHold? hold, DateTimeOffset now)
        => hold is not { } waiting ? null : waiting.Reason switch
        {
            LaunchHold.Paused => Strings.Hold_Paused,
            LaunchHold.AgentDisabled => Strings.Hold_AgentOff,
            LaunchHold.UsageLimit => Text.Format(Strings.Hold_Usage, Countdown(waiting.Until, now)),
            LaunchHold.WorkingDir => Text.Format(Strings.Hold_Folder, waiting.Blocker?.Number),
            LaunchHold.Dependency => Text.Format(Strings.Hold_Dependency, waiting.Blocker?.Number),
            LaunchHold.Slot => Text.Format(Strings.Hold_Slot, waiting.Used, waiting.Cap),
            _ => null,
        };

    private static string? HoldTipFor(ReadyHold? hold, string agent)
        => hold is not { } waiting ? null : waiting.Reason switch
        {
            LaunchHold.Paused => Strings.Hold_Paused_Tip,
            LaunchHold.AgentDisabled => Text.Format(Strings.Hold_AgentOff_Tip, agent),
            LaunchHold.UsageLimit => Text.Format(
                Strings.Hold_Usage_Tip,
                agent,
                waiting.Until?.ToLocalTime().ToString(Strings.Schedule_DateFormat, CultureInfo.CurrentCulture)),
            LaunchHold.WorkingDir => Text.Format(
                Strings.Hold_Folder_Tip, waiting.Blocker?.Number, waiting.Blocker?.Title),
            LaunchHold.Dependency => Text.Format(
                Strings.Hold_Dependency_Tip, waiting.Blocker?.Number, waiting.Blocker?.Title),
            LaunchHold.Slot => Text.Format(Strings.Hold_Slot_Tip, waiting.Used, waiting.Cap),
            _ => null,
        };

    // `paused` and `agent off` are the user's own doing rather than something in the way, so they
    // stay neutral; the rest carry the quiet tint that says "waiting on something".
    private static string HoldClassFor(ReadyHold? hold)
        => hold?.Reason is LaunchHold.Paused or LaunchHold.AgentDisabled
            ? "badge"
            : "badge b-hold";

    private static string Countdown(DateTimeOffset? until, DateTimeOffset now)
    {
        var left = (until ?? now) - now;

        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        if (left.TotalHours >= 24)
            return Text.Format(Strings.Hold_Days, (int)left.TotalDays, left.Hours);

        return left.TotalMinutes >= 60
            ? Text.Format(Strings.Hold_Hours, (int)left.TotalHours, left.Minutes)
            : Text.Format(Strings.Hold_Minutes, (int)left.TotalMinutes);
    }

    private static IReadOnlyList<string> MetricsFor(CardMetrics? metrics)
    {
        if (metrics is not { } m)
            return [];

        var parts = new List<string>();

        if (m.ContextPercent is not null)
            parts.Add(Text.Format(
                Strings.Metrics_Context,
                Text.Thousands(m.ContextUsed),
                Text.Thousands(m.ContextLimit)));

        if (m.TurnCount > 0)
            parts.Add(Text.Format(Strings.Metrics_Turns, m.TurnCount));

        if (m.Compactions > 0)
            parts.Add(Text.Format(Strings.Metrics_Compactions, m.Compactions));

        if (m.TokensTotal > 0)
            parts.Add(Text.Format(Strings.Metrics_Tokens, Text.Thousands(m.TokensTotal)));

        return parts;
    }
}
