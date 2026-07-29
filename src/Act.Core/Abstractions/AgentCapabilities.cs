using Act.Core.Model;

namespace Act.Core.Abstractions;

// What one agent can actually be asked for. `model` and `effort` are agent-specific values
// the adapter owns; `PermissionModes` says which of ACT's fixed set it can honour, so the
// UI never offers a mode that would be rejected at launch.
public sealed record AgentCapabilities(
    IReadOnlyList<string> Models,
    string? DefaultModel,
    IReadOnlyList<string> Efforts,
    IReadOnlySet<PermissionMode> PermissionModes);
