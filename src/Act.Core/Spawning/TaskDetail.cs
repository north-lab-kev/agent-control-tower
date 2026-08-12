using Act.Core.Model;
using Act.Core.Rules;

namespace Act.Core.Spawning;

// What `get_task` says about one card: the summary plus everything an agent could act on. Two
// omissions are deliberate rather than incidental — `sessionId`, because it is the join key ACT's
// own ingestion is keyed on and nothing an agent does needs it, and attachment paths, because a
// path is a grant and the read tools grant nothing.
public sealed record TaskDetail(
    string Id,
    int Number,
    string Title,
    string Prompt,
    string Column,
    string? Badge,
    string Agent,
    string? Model,
    string? Effort,
    string Permission,
    string WorkingDir,
    string Schedule,
    string Origin,
    string? ParentId,
    IReadOnlyList<string> Children,
    IReadOnlyList<string> DependsOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LaunchedAt,
    DateTimeOffset? CompletedAt,
    bool SpawnedByYou)
{
    public static TaskDetail From(Card card, Guid caller) => new(
        Id: card.Id.ToString(),
        Number: card.Number,
        Title: CardTitle.Of(card),
        Prompt: card.InitialPrompt,
        Column: TaskWords.Of(card.Column),
        Badge: TaskWords.Of(card.Badge),
        Agent: TaskWords.Of(card.AgentType),
        Model: card.LaunchConfig.Model,
        Effort: card.LaunchConfig.Effort,
        Permission: TaskWords.Of(card.LaunchConfig.PermissionMode),
        WorkingDir: card.WorkingDir,
        Schedule: TaskWords.Of(card.Schedule ?? TaskSchedule.Manual),
        Origin: card.Origin is TaskOrigin.Spawned ? "spawned" : "manual",
        ParentId: card.ParentId?.ToString(),
        Children: [.. card.Children.Select(id => id.ToString())],
        DependsOn: [.. card.DependsOn.Select(id => id.ToString())],
        CreatedAt: card.CreatedAt,
        LaunchedAt: card.LaunchedAt,
        CompletedAt: card.CompletedAt,
        SpawnedByYou: card.ParentId == caller);
}
