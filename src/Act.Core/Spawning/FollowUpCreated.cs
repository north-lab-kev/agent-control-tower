namespace Act.Core.Spawning;

// What `create_followup` hands back. The resolved values are on it rather than only the ids, because
// an agent that asked for a reasoning effort the model does not have needs to learn what it got —
// `adjustments` says which fields moved and why, and returning them is the read side of the
// never-silently-drop rule.
public sealed record FollowUpCreated(
    string Id,
    int Number,
    string Agent,
    string? Model,
    string? Effort,
    string Permission,
    string Schedule,
    IReadOnlyList<string> Adjustments,
    bool AlreadyExisted);
