using Act.App.Cards;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Board;

public partial class TaskDialog(
    BoardState board,
    IAgentCapabilityCatalog agents,
    IClock clock,
    DialogService dialogService)
{
    private NewTaskForm form = new();

    private bool saving;

    [Parameter]
    public Card? Card { get; set; }

    private static SettingChoice<AgentType>[] AgentChoices =>
    [
        new(AgentType.ClaudeCode, Strings.Agent_ClaudeCode),
        new(AgentType.Codex, Strings.Agent_Codex),
    ];

    private static SettingChoice<PermissionMode>[] PermissionChoices =>
    [
        new(PermissionMode.Default, Strings.Permission_Default),
        new(PermissionMode.Plan, Strings.Permission_Plan),
        new(PermissionMode.AcceptEdits, Strings.Permission_AcceptEdits),
        new(PermissionMode.Auto, Strings.Permission_Auto),
        new(PermissionMode.DontAsk, Strings.Permission_DontAsk),
        new(PermissionMode.Bypass, Strings.Permission_Bypass),
    ];

    private static SettingChoice<TaskSchedule>[] ScheduleChoices =>
    [
        new(TaskSchedule.Manual, Strings.ScheduleOption_Manual),
        new(TaskSchedule.Now, Strings.ScheduleOption_Now),
        new(TaskSchedule.NextWindow, Strings.ScheduleOption_NextWindow),
        new(TaskSchedule.WindowAfterNext, Strings.ScheduleOption_WindowAfterNext),
        new(TaskSchedule.SpecificDateTime, Strings.ScheduleOption_DateTime),
    ];

    private static SettingChoice<GitAction?>[] GitChoices =>
    [
        new(GitAction.Commit, Strings.GitAction_Commit),
        new(GitAction.Push, Strings.GitAction_Push),
        new(GitAction.PullRequest, Strings.GitAction_PullRequest),
    ];

    // Keeps a stored model visible even when the agent no longer offers it — otherwise editing
    // a card would show an empty dropdown for a value it is still carrying.
    private IReadOnlyList<string> Models
    {
        get
        {
            var models = agents.For(form.Agent).Models.Select(model => model.Slug).ToList();

            return form.Model is { } current && !models.Contains(current)
                ? [.. models, current]
                : models;
        }
    }

    // Narrows with the chosen model, because the ladders genuinely differ per model on Codex.
    // A stored effort survives the same way a stored model does.
    private IReadOnlyList<string> Efforts
    {
        get
        {
            var efforts = agents.For(form.Agent).EffortsFor(form.Model);

            return form.Effort is { } current && !efforts.Contains(current)
                ? [.. efforts, current]
                : efforts;
        }
    }

    private string PermissionHint => form.Permission switch
    {
        PermissionMode.Default => Strings.Permission_Default_Hint,
        PermissionMode.Plan => Strings.Permission_Plan_Hint,
        PermissionMode.AcceptEdits => Strings.Permission_AcceptEdits_Hint,
        PermissionMode.Auto => Strings.Permission_Auto_Hint,
        PermissionMode.DontAsk => Strings.Permission_DontAsk_Hint,
        PermissionMode.Bypass => Strings.Permission_Bypass_Hint,
        _ => string.Empty,
    };

    protected override void OnInitialized()
    {
        if (Card is { } card)
            form = NewTaskForm.From(card);
    }

    private void OnAgentChanged(AgentType agent)
    {
        form.Agent = agent;

        if (!Models.Contains(form.Model))
            form.Model = null;
    }

    private void OnScheduleChanged(TaskSchedule schedule)
    {
        form.Schedule = schedule;

        if (!form.WantsDateTime)
            form.ScheduledFor = null;
    }

    private void OnAutoCompleteChanged(bool autoComplete)
    {
        form.AutoComplete = autoComplete;

        if (autoComplete)
            return;

        form.SelectedGitAction = null;
        form.Draft = false;
    }

    private async Task OnSubmitAsync(NewTaskForm _)
    {
        if (saving)
            return;

        saving = true;

        try
        {
            if (Card is { } card)
            {
                form.ApplyTo(card);
                await board.UpdateAsync(card);
            }
            else
            {
                await board.CreateAsync(form.ToCard(clock.Now));
            }
        }
        finally
        {
            saving = false;
        }

        dialogService.Close(true);
    }

    private void Cancel() => dialogService.Close(false);
}
