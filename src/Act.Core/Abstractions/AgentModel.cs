namespace Act.Core.Abstractions;

// One model an agent offers, carrying its own reasoning-effort ladder. Effort is a property
// of the model rather than of the agent because the agents genuinely differ that way — in
// Codex's catalog `gpt-5.6-terra` reaches `ultra` while `gpt-5.5` stops at `xhigh` — so a
// single list per agent would offer the user efforts their chosen model cannot honour.
// An empty `Efforts` is how a model says it does not model effort at all.
public sealed record AgentModel(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Efforts,
    string? DefaultEffort = null);
