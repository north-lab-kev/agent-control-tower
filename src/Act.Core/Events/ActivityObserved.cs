namespace Act.Core.Events;

// Carries more weight than its name suggests: because the user acts in the terminal rather
// than in ACT, this is also how ACT learns a blocked session recovered. `UserPromptSubmit`
// and `PreToolUse` both land here, and the rules engine reads one of them arriving on a
// Needs-feedback or To-review card as "the user answered / sent it back".
public sealed record ActivityObserved(
    string SessionId,
    DateTimeOffset At,
    string? ToolName = null)
    : AgentEvent(SessionId, At);
