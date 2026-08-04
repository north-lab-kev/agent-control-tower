using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;

namespace Act.App.Cards;

// The vocabulary every form that describes a task shares — the task page, a template, and the agent
// sections in settings. Written out three times before templates existed and would have been four,
// which is one too many for a mapping whose whole job is that the same value reads the same way
// wherever it is offered.
public static class TaskLabels
{
    public static string Agent(AgentType agent) => agent switch
    {
        AgentType.Codex => Strings.Agent_Codex,
        _ => Strings.Agent_ClaudeCode,
    };

    public static string Permission(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => Strings.Permission_Default,
        PermissionMode.Plan => Strings.Permission_Plan,
        PermissionMode.AcceptEdits => Strings.Permission_AcceptEdits,
        PermissionMode.Auto => Strings.Permission_Auto,
        PermissionMode.DontAsk => Strings.Permission_DontAsk,
        PermissionMode.Bypass => Strings.Permission_Bypass,
        _ => mode.ToString(),
    };

    public static string PermissionHint(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => Strings.Permission_Default_Hint,
        PermissionMode.Plan => Strings.Permission_Plan_Hint,
        PermissionMode.AcceptEdits => Strings.Permission_AcceptEdits_Hint,
        PermissionMode.Auto => Strings.Permission_Auto_Hint,
        PermissionMode.DontAsk => Strings.Permission_DontAsk_Hint,
        PermissionMode.Bypass => Strings.Permission_Bypass_Hint,
        _ => string.Empty,
    };

    public static string Schedule(TaskSchedule schedule) => schedule switch
    {
        TaskSchedule.Now => Strings.ScheduleOption_Now,
        TaskSchedule.NextWindow => Strings.ScheduleOption_NextWindow,
        TaskSchedule.SpecificDateTime => Strings.ScheduleOption_DateTime,
        _ => Strings.ScheduleOption_Manual,
    };

    public static string Git(GitAction action) => action switch
    {
        GitAction.Push => Strings.GitAction_Push,
        GitAction.PullRequest => Strings.GitAction_PullRequest,
        _ => Strings.GitAction_Commit,
    };

    // The default template's name is not stored: it is not the user's to change, and a stored
    // translation would freeze whichever language the install first ran in. Every other template
    // requires a name, so this is the only case with nothing to show.
    public static string Template(TaskTemplate template)
        => template.IsDefault ? Strings.Templates_Default : template.Name;

    // A specific datetime describes one task, never a kind of task, so the forms that pre-fill a task
    // rather than being one do not offer it.
    public static SettingChoice<TaskSchedule>[] Schedules(bool withDateTime)
        => withDateTime
            ?
            [
                Choice(TaskSchedule.Manual),
                Choice(TaskSchedule.Now),
                Choice(TaskSchedule.NextWindow),
                Choice(TaskSchedule.SpecificDateTime),
            ]
            :
            [
                Choice(TaskSchedule.Manual),
                Choice(TaskSchedule.Now),
                Choice(TaskSchedule.NextWindow),
            ];

    public static SettingChoice<GitAction?>[] GitActions() =>
    [
        new(GitAction.Commit, Strings.GitAction_Commit),
        new(GitAction.Push, Strings.GitAction_Push),
        new(GitAction.PullRequest, Strings.GitAction_PullRequest),
    ];

    private static SettingChoice<TaskSchedule> Choice(TaskSchedule schedule) => new(schedule, Schedule(schedule));
}
