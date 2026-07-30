using Act.Core.Abstractions;

namespace Act.TestSupport;

public sealed record AgentInput(
    AgentInputKind Kind,
    string? Text = null,
    TerminalSize? Size = null);
