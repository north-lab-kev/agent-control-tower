using Act.Core.Model;

namespace Act.App;

// Mock board data for the static UI draft. Replaced by the LiteDB store in roadmap step 3.
public static class SampleBoard
{
    public static IReadOnlyList<Card> Cards { get; } =
    [
        new Card
        {
            Number = 1051,
            Title = "Spec rate-limit middleware",
            Column = BoardColumn.Preparing,
            WorkingDir = "~/dev/api-gateway",
        },
        new Card
        {
            Number = 1050,
            Title = "Draft auth refactor prompt",
            Column = BoardColumn.Preparing,
            WorkingDir = "~/dev/act",
        },
        new Card
        {
            Number = 1048,
            Title = "Dark-mode toggle",
            Column = BoardColumn.Ready,
            Schedule = TaskSchedule.Now,
            WorkingDir = "~/dev/act",
        },
        new Card
        {
            Number = 1047,
            Title = "Migrate to LiteDB",
            Column = BoardColumn.Ready,
            Schedule = TaskSchedule.NextWindow,
            WorkingDir = "~/dev/act",
        },
        new Card
        {
            Number = 1046,
            Title = "Bump deps",
            Column = BoardColumn.Ready,
            Schedule = TaskSchedule.SpecificDateTime,
            ScheduledFor = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero),
            WorkingDir = "~/dev/act",
        },
        new Card
        {
            Number = 1042,
            Title = "Refactor session-binding logic",
            Column = BoardColumn.Executing,
            Badge = Badge.Running,
            ObservedModel = "opus 4.8",
            WorkingDir = "~/dev/act",
            Metrics = new CardMetrics
            {
                ContextUsed = 142_000,
                ContextLimit = 200_000,
                TurnCount = 18,
                Cost = 0.74m,
            },
        },
        new Card
        {
            Number = 1040,
            Title = "Index 12k JSONL transcripts",
            Column = BoardColumn.Executing,
            Badge = Badge.Stale,
            ObservedModel = "sonnet 5",
            WorkingDir = "~/dev/act",
            Metrics = new CardMetrics
            {
                ContextUsed = 88_000,
                ContextLimit = 200_000,
                TurnCount = 7,
                Compactions = 2,
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-6),
            },
        },
        new Card
        {
            Number = 1044,
            Title = "Delete legacy /v1 endpoints",
            Column = BoardColumn.NeedsFeedback,
            Badge = Badge.NeedsPermission,
            ObservedModel = "opus 4.8",
            WorkingDir = "~/dev/api-gateway",
            Metrics = new CardMetrics { ContextUsed = 41_000, ContextLimit = 200_000, TurnCount = 4 },
        },
        new Card
        {
            Number = 1043,
            Title = "Caching layer for plant lookups?",
            Column = BoardColumn.NeedsFeedback,
            Badge = Badge.NeedsAnswer,
            ObservedModel = "sonnet 5",
            WorkingDir = "~/dev/act",
            Metrics = new CardMetrics { ContextUsed = 12_000, ContextLimit = 200_000, TurnCount = 2 },
        },
        new Card
        {
            Number = 1038,
            Title = "Radzen grid virtualization crash",
            Column = BoardColumn.NeedsFeedback,
            Badge = Badge.Error,
            ObservedModel = "opus 4.8",
            WorkingDir = "~/dev/act",
        },
        new Card
        {
            Number = 1039,
            Title = "Implement flight-strip board",
            Column = BoardColumn.ToReview,
            Badge = Badge.Idle,
            ObservedModel = "opus 4.8",
            WorkingDir = "~/dev/act",
            AutoComplete = true,
            ChildCount = 3,
            Metrics = new CardMetrics
            {
                ContextUsed = 96_000,
                ContextLimit = 200_000,
                TurnCount = 22,
                Cost = 1.12m,
            },
        },
        new Card
        {
            Number = 1035,
            Title = "Release build script",
            Column = BoardColumn.Completed,
            WorkingDir = "~/dev/act",
            Metrics = new CardMetrics { TurnCount = 9, Cost = 0.38m },
        },
        new Card
        {
            Number = 1031,
            Title = "Empty app shell",
            Column = BoardColumn.Completed,
            WorkingDir = "~/dev/act",
            Metrics = new CardMetrics { TurnCount = 5, Cost = 0.16m },
        },
    ];
}
