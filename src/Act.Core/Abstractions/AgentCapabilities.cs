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
//
// `UtilityModel` is the one model the *user* never chooses: the cheapest thing the agent offers, for
// the questions ACT asks on its own account rather than as the user's work. It is separate from
// `DefaultModel` because the two answer opposite questions — the default is what the work should run
// on when nobody said, and this is what a throwaway is allowed to cost.
public sealed record AgentCapabilities(
    IReadOnlyList<AgentModel> Models,
    string? DefaultModel,
    IReadOnlyList<PermissionMode> PermissionModes,
    bool DesktopHandoff = false,
    string? UtilityModel = null)
{
    public bool Supports(PermissionMode mode) => PermissionModes.Contains(mode);

    // Falls back to the default rather than to nothing: an agent that has not named a cheap model
    // still has to be able to answer, and the launch resolver would fill a null in anyway.
    public AgentModel? Utility => Model(UtilityModel) ?? Model(DefaultModel) ?? Models.FirstOrDefault();

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
