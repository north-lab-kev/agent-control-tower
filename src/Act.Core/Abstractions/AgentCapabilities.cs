using Act.Core.Model;

namespace Act.Core.Abstractions;

// What one agent can actually be asked for. Models are agent-specific values the adapter
// owns, each carrying its own effort ladder; `PermissionModes` says which of ACT's fixed set
// it can honour, so the UI never offers a mode that would be rejected at launch.
// `DesktopHandoff` lets the UI decide whether to show that action before any session exists
// to build a url from.
public sealed record AgentCapabilities(
    IReadOnlyList<AgentModel> Models,
    string? DefaultModel,
    IReadOnlySet<PermissionMode> PermissionModes,
    bool DesktopHandoff = false)
{
    public AgentModel? Model(string? slug) => slug is null
        ? null
        : Models.FirstOrDefault(model => model.Slug == slug);

    public IReadOnlyList<string> EffortsFor(string? slug) => Model(slug)?.Efforts ?? [];
}
