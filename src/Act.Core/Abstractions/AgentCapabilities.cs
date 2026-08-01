using Act.Core.Model;

namespace Act.Core.Abstractions;

// What one agent can actually be asked for. Models are agent-specific values the adapter
// owns, each carrying its own effort ladder; `PermissionModes` is the same idea for permissions —
// the adapter declares which of ACT's normalized modes it offers, **in the order the UI should show
// them**, so the form never lists a mode that would be rejected at launch and an agent can gain or
// drop one without the UI knowing anything about it.
//
// The modes stay a shared enum rather than agent-owned strings, and that is deliberate: the value is
// persisted on the card and has to keep meaning if the card is retargeted at the other agent. What
// each CLI does to honour one is the adapter's business — see `CodexPermissions`, which spends two
// flags on what Claude Code expresses in one.
//
// `DesktopHandoff` lets the UI decide whether to show that action before any session exists
// to build a url from.
public sealed record AgentCapabilities(
    IReadOnlyList<AgentModel> Models,
    string? DefaultModel,
    IReadOnlyList<PermissionMode> PermissionModes,
    bool DesktopHandoff = false)
{
    public bool Supports(PermissionMode mode) => PermissionModes.Contains(mode);

    public AgentModel? Model(string? slug) => slug is null
        ? null
        : Models.FirstOrDefault(model => model.Slug == slug);

    public IReadOnlyList<string> EffortsFor(string? slug) => Model(slug)?.Efforts ?? [];

    // The model an *observed* id names. A transcript reports what the API answered with
    // (`claude-opus-5`), not the alias the launch asked for (`opus`), so an exact match is tried
    // first and a contained slug second.
    public AgentModel? ModelFor(string? observed) => observed is null
        ? null
        : Model(observed)
            ?? Models.FirstOrDefault(
                model => observed.Contains(model.Slug, StringComparison.OrdinalIgnoreCase));

    public int? ContextLimitFor(string? observed) => ModelFor(observed)?.ContextLimit;
}
