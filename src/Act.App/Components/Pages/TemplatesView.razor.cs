using Act.App.Cards;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Pages;

// The saved starting points a task can be created from, as a list rather than a board — the same
// shape the archive takes, and for the same reason: these are not work in progress. It replaced the
// single *New task defaults* block in settings, which could only ever describe one kind of task.
public partial class TemplatesView(
    UserSettingsService settings,
    DialogService dialogService,
    NavigationManager navigation)
{
    private bool deleting;

    private IReadOnlyList<TaskTemplate> Rows => settings.Templates;

    private void OpenNew() => navigation.NavigateTo("/template/new");

    private void Open(TaskTemplate template) => navigation.NavigateTo($"/template/{template.Id}");

    private async Task DeleteAsync(TaskTemplate template)
    {
        if (deleting)
            return;

        deleting = true;

        try
        {
            var confirmed = await dialogService.Confirm(
                Text.Format(Strings.Templates_DeleteConfirm, TaskLabels.Template(template)),
                Strings.Templates_Delete,
                new ConfirmOptions
                {
                    OkButtonText = Strings.Templates_DeleteYes,
                    CancelButtonText = Strings.NewTask_Cancel,
                    CssClass = "act-dialog",
                });

            if (confirmed is true)
                settings.DeleteTemplate(template.Id);
        }
        finally
        {
            deleting = false;
        }
    }

    private void BackToBoard() => navigation.NavigateTo("/");

    // Enough to recognise a template without opening it, which is the whole job of the row: what it
    // runs, where, and how much of the prompt it already carries.
    private static string Meta(TaskTemplate template)
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(template.Title)
                ? null
                : Text.Format(Strings.Templates_TitleMeta, template.Title),
            TaskLabels.Agent(template.Agent),
            template.Model,
            TaskLabels.Permission(template.PermissionMode),
            TaskLabels.Schedule(template.Schedule),
            template.WorkingDir,
            string.IsNullOrWhiteSpace(template.Prompt) ? Strings.Templates_NoPrompt : null,
        };

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
