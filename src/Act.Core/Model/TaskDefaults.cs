namespace Act.Core.Model;

// What a new task starts as. Everything the form asks for **except the title and the prompt** —
// those are the task, and pre-filling them would mean creating the same task twice by accident.
//
// Nullable where "no preference" is a real answer: a null model means the agent's own default, and
// a null git action means none, both of which are different from a value that happens to be first
// in a list. `Agent` and `PermissionMode` have no such gap — a card must have one of each — so they
// carry the plain value and the shipped defaults match what the form used before this existed.
public sealed class TaskDefaults
{
    public string WorkingDir { get; set; } = string.Empty;

    public AgentType Agent { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public PermissionMode PermissionMode { get; set; }

    public TaskSchedule Schedule { get; set; } = TaskSchedule.Manual;

    public GitAction? GitAction { get; set; }

    public bool Draft { get; set; }

    public TaskDefaults Copy() => new()
    {
        WorkingDir = WorkingDir,
        Agent = Agent,
        Model = Model,
        Effort = Effort,
        PermissionMode = PermissionMode,
        Schedule = Schedule,
        GitAction = GitAction,
        Draft = Draft,
    };
}
