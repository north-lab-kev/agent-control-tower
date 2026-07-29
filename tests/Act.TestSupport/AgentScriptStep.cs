using Act.Core.Events;

namespace Act.TestSupport;

internal sealed record AgentScriptStep(
    Func<string, DateTimeOffset, AgentEvent>? Event,
    AgentInputKind? Awaits);
