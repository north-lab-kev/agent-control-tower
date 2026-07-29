using Act.Core.Model;

namespace Act.App.Seeding;

// Demo content for the board while task creation is still to come (roadmap step 5).
public static class SampleCards
{
    public static IReadOnlyList<Card> Create()
    {
        var now = DateTimeOffset.UtcNow;
        var scheduled = new DateTimeOffset(now.Date.AddDays(2).AddHours(9), now.Offset);

        var board = new Card
        {
            Id = IdFor(1039),
            Number = 1039,
            Title = "Implement flight-strip board",
            InitialPrompt = "Build the six-column board with flight-strip cards.",
            Column = BoardColumn.ToReview,
            Badge = Badge.Idle,
            SessionId = "b7f1c2d4-0e58-4a19-9c33-2f6d5a814e07",
            ObservedModel = "opus 4.8",
            WorkingDir = "~/dev/act",
            AutoComplete = true,
            CreatedAt = now.AddHours(-9),
            LaunchedAt = now.AddHours(-8),
            LaunchConfig = new LaunchConfig { Model = "opus", PermissionMode = PermissionMode.AcceptEdits },
            Children = [IdFor(1048), IdFor(1047), IdFor(1046)],
            LastMessage = "Board renders all six columns; density toggle persists.",
            Metrics = new CardMetrics
            {
                ContextUsed = 96_000,
                ContextLimit = 200_000,
                TurnCount = 22,
                ToolCalls = 134,
                TokensIn = 486_000,
                TokensOut = 61_500,
                Cost = 1.12m,
                ActiveTime = TimeSpan.FromMinutes(74),
                LastActivityAt = now.AddMinutes(-35),
            },
        };

        return
        [
            new Card
            {
                Id = IdFor(1051),
                Number = 1051,
                Title = "Spec rate-limit middleware",
                InitialPrompt = "Draft the middleware design for per-tenant rate limiting.",
                Column = BoardColumn.Preparing,
                WorkingDir = "~/dev/api-gateway",
                CreatedAt = now.AddMinutes(-25),
            },
            new Card
            {
                Id = IdFor(1050),
                Number = 1050,
                Title = "Draft auth refactor prompt",
                InitialPrompt = "Outline the auth refactor before touching any code.",
                Column = BoardColumn.Preparing,
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddMinutes(-40),
                LaunchConfig = new LaunchConfig { PermissionMode = PermissionMode.Plan },
            },
            Spawned(1048, "Dark-mode toggle", "Add the light/dark theme override to settings.", board, TaskSchedule.Now, now),
            Spawned(1047, "Migrate to LiteDB", "Persist the card model in LiteDB.", board, TaskSchedule.NextWindow, now),
            ScheduledChild(1046, "Bump deps", "Update the NuGet dependencies and rerun the suite.", board, scheduled, now),
            new Card
            {
                Id = IdFor(1042),
                Number = 1042,
                Title = "Refactor session-binding logic",
                InitialPrompt = "Rework how sessions bind to cards on launch.",
                Column = BoardColumn.Executing,
                Badge = Badge.Running,
                SessionId = "3c9a51e6-77b2-4d0f-8a41-9e5b0c72d183",
                ObservedModel = "opus 4.8",
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddHours(-2),
                LaunchedAt = now.AddMinutes(-52),
                LaunchConfig = new LaunchConfig { Model = "opus", Effort = "high" },
                Metrics = new CardMetrics
                {
                    ContextUsed = 142_000,
                    ContextLimit = 200_000,
                    TurnCount = 18,
                    ToolCalls = 97,
                    TokensIn = 312_400,
                    TokensOut = 44_100,
                    Cost = 0.74m,
                    ActiveTime = TimeSpan.FromMinutes(41),
                    LastActivityAt = now.AddSeconds(-20),
                },
            },
            new Card
            {
                Id = IdFor(1040),
                Number = 1040,
                Title = "Index 12k JSONL transcripts",
                InitialPrompt = "Build a searchable index over the stored transcripts.",
                Column = BoardColumn.Executing,
                Badge = Badge.Stale,
                SessionId = "f04d7a92-1b6c-4e83-b5d0-6a29c8f14b55",
                ObservedModel = "sonnet 5",
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddHours(-3),
                LaunchedAt = now.AddHours(-1),
                Metrics = new CardMetrics
                {
                    ContextUsed = 88_000,
                    ContextLimit = 200_000,
                    TurnCount = 7,
                    ToolCalls = 51,
                    Compactions = 2,
                    TokensIn = 204_800,
                    TokensOut = 18_300,
                    Cost = 0.31m,
                    LastActivityAt = now.AddMinutes(-6),
                },
            },
            new Card
            {
                Id = IdFor(1044),
                Number = 1044,
                Title = "Delete legacy /v1 endpoints",
                InitialPrompt = "Remove the deprecated /v1 controllers and their tests.",
                Column = BoardColumn.NeedsFeedback,
                Badge = Badge.NeedsPermission,
                SessionId = "9e2b6c17-4a80-4f5d-92c1-0d7e3b845a6f",
                ObservedModel = "opus 4.8",
                WorkingDir = "~/dev/api-gateway",
                CreatedAt = now.AddHours(-4),
                LaunchedAt = now.AddMinutes(-28),
                LastMessage = "Requesting permission to delete src/Api/V1.",
                Metrics = new CardMetrics
                {
                    ContextUsed = 41_000,
                    ContextLimit = 200_000,
                    TurnCount = 4,
                    ToolCalls = 12,
                    LastActivityAt = now.AddMinutes(-3),
                },
            },
            new Card
            {
                Id = IdFor(1043),
                Number = 1043,
                Title = "Caching layer for plant lookups?",
                InitialPrompt = "Evaluate whether plant lookups need a cache.",
                Column = BoardColumn.NeedsFeedback,
                Badge = Badge.NeedsAnswer,
                SessionId = "7a5f0d38-6c94-4b21-8e07-15c9d2a63f4e",
                ObservedModel = "sonnet 5",
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddHours(-5),
                LaunchedAt = now.AddMinutes(-46),
                LastMessage = "Should the cache be per-plant or global?",
                Metrics = new CardMetrics
                {
                    ContextUsed = 12_000,
                    ContextLimit = 200_000,
                    TurnCount = 2,
                    ToolCalls = 5,
                    LastActivityAt = now.AddMinutes(-11),
                },
            },
            new Card
            {
                Id = IdFor(1038),
                Number = 1038,
                Title = "Radzen grid virtualization crash",
                InitialPrompt = "Reproduce and fix the grid crash on virtualized scroll.",
                Column = BoardColumn.NeedsFeedback,
                Badge = Badge.Error,
                SessionId = "1d6e8b45-3f27-4c90-a8b3-77e05c419d2a",
                ObservedModel = "opus 4.8",
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddHours(-6),
                LaunchedAt = now.AddHours(-1),
                LastMessage = "Process exited with code 1.",
            },
            board,
            new Card
            {
                Id = IdFor(1035),
                Number = 1035,
                Title = "Release build script",
                InitialPrompt = "Create a repeatable release build and packaging script.",
                Column = BoardColumn.Completed,
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddDays(-2),
                LaunchedAt = now.AddDays(-2).AddMinutes(10),
                CompletedAt = now.AddDays(-2).AddHours(1),
                AutoComplete = true,
                AutoGit = new AutoGitOptions { Action = GitAction.Push },
                Metrics = new CardMetrics { TurnCount = 9, ToolCalls = 40, Cost = 0.38m },
            },
            new Card
            {
                Id = IdFor(1031),
                Number = 1031,
                Title = "Empty app shell",
                InitialPrompt = "Stand up a blank Blazor Server app on .NET 10.",
                Column = BoardColumn.Completed,
                WorkingDir = "~/dev/act",
                CreatedAt = now.AddDays(-3),
                LaunchedAt = now.AddDays(-3).AddMinutes(5),
                CompletedAt = now.AddDays(-3).AddMinutes(40),
                Metrics = new CardMetrics { TurnCount = 5, ToolCalls = 21, Cost = 0.16m },
            },
        ];
    }

    private static Card Spawned(
        int number, string title, string prompt, Card parent, TaskSchedule schedule, DateTimeOffset now)
        => new()
        {
            Id = IdFor(number),
            Number = number,
            Title = title,
            InitialPrompt = prompt,
            Column = BoardColumn.Ready,
            Schedule = schedule,
            WorkingDir = parent.WorkingDir,
            Origin = TaskOrigin.Spawned,
            ParentId = parent.Id,
            SpawnAuthor = SpawnAuthor.Agent,
            CreatedAt = now.AddMinutes(-50),
        };

    private static Card ScheduledChild(
        int number, string title, string prompt, Card parent, DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        var card = Spawned(number, title, prompt, parent, TaskSchedule.SpecificDateTime, now);
        card.ScheduledFor = scheduledFor;

        return card;
    }

    private static Guid IdFor(int number) => new($"00000000-0000-0000-0000-{number:D12}");
}
