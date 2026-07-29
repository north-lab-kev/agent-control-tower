using Act.Core.Abstractions;

namespace Act.TestSupport;

public sealed record AgentInput(
    AgentInputKind Kind,
    string? RequestId = null,
    string? Text = null,
    PermissionDecision? Decision = null);
