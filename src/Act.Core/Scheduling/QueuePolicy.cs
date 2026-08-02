using Act.Core.Model;

namespace Act.Core.Scheduling;

// The user's settings as the queue sees them, so the rule takes one argument instead of four and
// the core never has to know where they are stored.
public sealed record QueuePolicy(
    int MaxConcurrent,
    bool AutoExecutionPaused,
    bool PreventConcurrentWorkingDir,
    IReadOnlySet<AgentType> EnabledAgents);
