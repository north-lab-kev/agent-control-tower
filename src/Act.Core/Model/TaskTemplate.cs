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

    public bool AllowConcurrentWorkingDir { get; set; }

    // A template may carry "no folder needed" like any other launch setting, so somebody who asks a lot of
    // questions can save their own starting point for them — see `Card.NoWorkingDir`.
    public bool NoWorkingDir { get; set; }

    public TaskTemplate Copy() => new()
    {
        Id = Id,
        Name = Name,
        IsDefault = IsDefault,
        NoWorkingDir = NoWorkingDir,
        Title = Title,
        Prompt = Prompt,
        WorkingDir = WorkingDir,
        Agent = Agent,
        Model = Model,
        Effort = Effort,
        PermissionMode = PermissionMode,
        Schedule = Schedule,
        AllowConcurrentWorkingDir = AllowConcurrentWorkingDir,
    };
}
