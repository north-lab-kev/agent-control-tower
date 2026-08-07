namespace Act.Core.Model;

public sealed class TaskTemplate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string WorkingDir { get; set; } = string.Empty;

    public AgentType Agent { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public PermissionMode PermissionMode { get; set; }

    public TaskSchedule Schedule { get; set; } = TaskSchedule.Manual;

    public GitAction? GitAction { get; set; }

    public bool Draft { get; set; }

    public bool AllowConcurrentWorkingDir { get; set; }

    public TaskTemplate Copy() => new()
    {
        Id = Id,
        Name = Name,
        IsDefault = IsDefault,
        Title = Title,
        Prompt = Prompt,
        WorkingDir = WorkingDir,
        Agent = Agent,
        Model = Model,
        Effort = Effort,
        PermissionMode = PermissionMode,
        Schedule = Schedule,
        GitAction = GitAction,
        Draft = Draft,
        AllowConcurrentWorkingDir = AllowConcurrentWorkingDir,
    };
}
