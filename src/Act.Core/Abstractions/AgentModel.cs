namespace Act.Core.Abstractions;

// One model an agent offers, carrying its own reasoning-effort ladder. Effort is a property
// of the model rather than of the agent because the agents genuinely differ that way — in
// Codex's catalog `gpt-5.6-terra` reaches `ultra` while `gpt-5.5` stops at `xhigh` — so a
// single list per agent would offer the user efforts their chosen model cannot honour.
// An empty `Efforts` is how a model says it does not model effort at all.
// `ContextLimit` is the model's context window, and it lives here because it is the same kind of
// fact as the effort ladder: agent-specific, per-model, and known only to the adapter. Nothing
// observed reports it — a transcript says how much context a request carried, never how much there
// was — so this is what turns that number into a percentage. Null means unknown, and an unknown
// limit shows no percentage rather than a wrong one.
public sealed record AgentModel(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Efforts,
    string? DefaultEffort = null,
    int? ContextLimit = null);
