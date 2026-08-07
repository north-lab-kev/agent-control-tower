using Act.Core.Model;

namespace Act.Core.Spawning;

// What `list_tasks` says about one card. Deliberately thin: an id to depend on, a number and title
// to recognise it by, and where it stands. No prompt, no path, no metrics — a list is for finding a
// card, and everything heavier is one `get_task` away.
public sealed record TaskSummary(
    string Id,
    int Number,
    string Title,
    string Column,
    string? Badge,
    string Agent,
    bool SpawnedByYou)
{
    public static TaskSummary From(Card card, Guid caller) => new(
        Id: card.Id.ToString(),
        Number: card.Number,
        Title: card.Title,
        Column: TaskWords.Of(card.Column),
        Badge: TaskWords.Of(card.Badge),
        Agent: TaskWords.Of(card.AgentType),
        SpawnedByYou: card.ParentId == caller);
}
