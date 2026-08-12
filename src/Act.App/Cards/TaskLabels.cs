using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
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

    // A model reads as the name its own CLI's picker shows — "Sonnet 5", not the `sonnet` slug the
    // command line carries — because the version is real information: Haiku is 4.5 while the rest are
    // 5, and the slug hides that. An id no capability list claims is shown verbatim: a real id the
    // user can look up beats a guess.
    public static string Model(AgentCapabilities? capabilities, string model)
        => capabilities?.ModelFor(model)?.DisplayName ?? model;

    // Keeps a stored model visible even when the agent no longer offers it — otherwise editing a
    // card would show an empty dropdown for a value it is still carrying. That one is named by its
    // slug, because a retired model has no display name left to borrow.
    public static SettingChoice<string>[] Models(AgentCapabilities capabilities, string? current)
    {
        var offered = capabilities.Models.Select(
            model => new SettingChoice<string>(model.Slug, model.DisplayName));

        return current is not null && capabilities.Model(current) is null
            ? [.. offered, new SettingChoice<string>(current, current)]
            : [.. offered];
    }

    public static string Schedule(TaskSchedule schedule) => schedule switch
    {
        TaskSchedule.Now => Strings.ScheduleOption_Now,
        TaskSchedule.NextWindow => Strings.ScheduleOption_NextWindow,
        TaskSchedule.SpecificDateTime => Strings.ScheduleOption_DateTime,
        _ => Strings.ScheduleOption_Manual,
    };

    // Powers of 1024 with the short units, because the number is read beside a file name to answer
    // "is this the big one or the small one" — not to be added up. Culture-aware so a French decimal
    // comma arrives as one.
    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
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

    private static SettingChoice<TaskSchedule> Choice(TaskSchedule schedule) => new(schedule, Schedule(schedule));
}
