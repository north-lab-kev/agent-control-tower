using Act.App.Cards;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// One template, as the task form minus the parts that belong to a single task: no title, because a
// name that describes a kind of work is the template's own, and no specific datetime, because that
// is a moment rather than a habit. A Save rather than settings' apply-as-you-type, since `/template/new`
// has nothing to apply to until the user commits it.
public partial class TemplateView(
    UserSettingsService settings,
    IAgentCapabilityCatalog agents,
    NavigationManager navigation)
{
    private TaskTemplate template = new();

    private bool missing;

    private bool picking;

    [Parameter]
    public Guid? TemplateId { get; set; }

    // Fixed when the page loads rather than read off the live form: the heading is where you are, and
    // clearing the name box to retype it must not blank the strip you are standing on.
    private string heading = string.Empty;

    private string PageHeading => heading;

    private bool FolderGuardOn => settings.PreventConcurrentWorkingDir;

    private IReadOnlyList<SettingChoice<AgentType>> AgentChoices
    {
        get
        {
            var enabled = settings.EnabledAgents;

            var offered = enabled.Contains(template.Agent) ? enabled : [.. enabled, template.Agent];

            return [.. offered.Select(agent => new SettingChoice<AgentType>(agent, TaskLabels.Agent(agent)))];
        }
    }

    private IReadOnlyList<SettingChoice<PermissionMode>> PermissionChoices
    {
        get
        {
            var offered = agents.For(template.Agent).PermissionModes;

            var modes = offered.Contains(template.PermissionMode)
                ? offered
                : [.. offered, template.PermissionMode];

            return [.. modes.Select(mode => new SettingChoice<PermissionMode>(mode, TaskLabels.Permission(mode)))];
        }
    }

    private static SettingChoice<TaskSchedule>[] ScheduleChoices => TaskLabels.Schedules(withDateTime: false);

    private static SettingChoice<GitAction?>[] GitChoices => TaskLabels.GitActions();

    private string PermissionHint => TaskLabels.PermissionHint(template.PermissionMode);

    private IReadOnlyList<string> Models
    {
        get
        {
            var models = agents.For(template.Agent).Models.Select(model => model.Slug).ToList();

            return template.Model is { } current && !models.Contains(current)
                ? [.. models, current]
                : models;
        }
    }

    private IReadOnlyList<string> Efforts
    {
        get
        {
            var efforts = OfferedEfforts;

            return template.Effort is { } current && !efforts.Contains(current)
                ? [.. efforts, current]
                : efforts;
        }
    }

    private IReadOnlyList<string> OfferedEfforts
    {
        get
        {
            var capabilities = agents.For(template.Agent);

            return capabilities.EffortsFor(template.Model ?? capabilities.DefaultModel);
        }
    }

    // A route can be reached with a stale id — a bookmark, a back button after a delete — so a
    // template that is no longer there gets the same not-found branch a card does.
    protected override void OnParametersSet()
    {
        if (TemplateId is not { } id)
        {
            template = new TaskTemplate { Agent = settings.EnabledAgents.FirstOrDefault() };
            heading = Strings.TemplateView_NewTitle;
            missing = false;

            return;
        }

        var stored = settings.Template(id);

        missing = stored is null;
        template = stored ?? new TaskTemplate();
        heading = TaskLabels.Template(template);
    }

    private void OnFolderPicked(string path)
    {
        template.WorkingDir = path;
        picking = false;
    }

    // The same rule the task form follows: model and effort are agent-specific, so switching agent
    // drops values the new one may not offer rather than storing something a launch would reject.
    private void OnAgentChanged(AgentType agent)
    {
        template.Agent = agent;

        var capabilities = agents.For(agent);

        if (template.Model is { } model && capabilities.Model(model) is null)
            template.Model = null;

        DropUnavailableEffort();

        if (!capabilities.Supports(template.PermissionMode))
            template.PermissionMode = PermissionMode.Default;
    }

    private void OnModelChanged(string? model)
    {
        template.Model = model;

        DropUnavailableEffort();
    }

    private void DropUnavailableEffort()
    {
        if (template.Effort is { } effort && !OfferedEfforts.Contains(effort))
            template.Effort = null;
    }

    private void OnGitActionChanged(GitAction? action)
    {
        template.GitAction = action;

        if (action is not GitAction.PullRequest)
            template.Draft = false;
    }

    private void OnSubmit(TaskTemplate _)
    {
        settings.SaveTemplate(template);

        BackToTemplates();
    }

    private void BackToTemplates() => navigation.NavigateTo("/templates");

    private void OnEscape()
    {
        if (picking)
        {
            picking = false;

            return;
        }

        BackToTemplates();
    }
}
