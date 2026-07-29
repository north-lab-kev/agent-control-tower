using Act.App.Cards;
using Act.App.Resources;
using Act.App.Components.Shared;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Pages;

// Creating and editing a task, as a page rather than a modal — the same shape as the session
// view, so a card is always a route you can land on, link to and come back from. One component
// serves both routes: `CardId` null is a create, set is an edit, and the only differences are
// the heading and which `BoardState` call the save makes.
public partial class TaskView(
    BoardState board,
    SessionRegistry registry,
    IAgentCapabilityCatalog agents,
    IWorkingDirectories directories,
    IClock clock,
    DialogService dialogService,
    NavigationManager navigation,
    NotificationService notifications)
{
    private NewTaskForm form = new();

    private Card? card;

    private bool missing;

    private bool saving;

    private bool picking;

    private bool deleting;

    [Parameter]
    public Guid? CardId { get; set; }

    private string PageHeading => card is { } existing ? existing.Title : Strings.TaskView_NewTitle;

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

    private PathCheck Dir => directories.Check(form.WorkingDir);

    // Malformed blocks the save; merely missing does not, because a directory you are about to
    // create is a perfectly reasonable thing to save a task against.
    private bool WorkingDirValid => string.IsNullOrWhiteSpace(form.WorkingDir) || Dir.WellFormed;

    private string WorkingDirError => Dir.Error switch
    {
        PathError.NotAbsolute => Strings.NewTask_DirNotAbsolute,
        _ => Strings.NewTask_DirMalformed,
    };

    private bool WorkingDirMissing
    {
        get
        {
            var check = Dir;

            return check.WellFormed && !check.Exists;
        }
    }

    // Shows the resolved path too, because the typed one and the real one differ whenever a `~`
    // or a forward slash is involved, and that difference is exactly what confuses people.
    private string WorkingDirMissingText
    {
        get
        {
            var resolved = Dir.Resolved;

            return resolved == form.WorkingDir
                ? Strings.NewTask_DirMissing
                : Text.Format(Strings.NewTask_DirMissingResolved, resolved);
        }
    }

    // The picked path is absolute and real, so it replaces whatever was typed rather than being
    // merged with it, and the picker closes — it has done its job.
    private void OnFolderPicked(string path)
    {
        form.WorkingDir = path;
        picking = false;
    }

    // Only the follow-ups that are still live. Ones already archived are not a decision the user
    // needs to make again.
    private IReadOnlyList<Card> LiveFollowUps => card is { } existing
        ? [.. board.ChildrenOf(existing).Where(child => !child.IsDeleted)]
        : [];

    // Straight to the archive when nothing hangs off the card; otherwise ask what should happen to
    // the follow-ups, which is a genuine choice rather than an "are you sure".
    private async Task StartDeleteAsync()
    {
        if (card is not { } existing)
            return;

        var followUps = LiveFollowUps;

        if (followUps.Count == 0)
        {
            await DeleteAsync(includeChildren: false);

            return;
        }

        var choice = await dialogService.OpenAsync<ArchiveFollowUpsDialog>(
            Strings.Task_Delete,
            new Dictionary<string, object?>
            {
                [nameof(ArchiveFollowUpsDialog.Card)] = existing,
                [nameof(ArchiveFollowUpsDialog.FollowUps)] = followUps,
            },
            new DialogOptions { Width = "460px", CloseDialogOnOverlayClick = true, CssClass = "act-dialog" });

        // Null when dismissed with the X or the overlay, which means the same as Cancel.
        if (choice is FollowUpChoice.WithFollowUps or FollowUpChoice.KeepFollowUps)
            await DeleteAsync(includeChildren: choice is FollowUpChoice.WithFollowUps);
    }

    // Kills first, on purpose: archiving the card while its process ran would leave an agent
    // working in a directory with nothing on the board pointing at it — the one state ACT exists
    // to prevent. Archiving is reversible; the process it was driving is not, so the session ends
    // either way and a restored card starts from Ready rather than mid-turn.
    private async Task DeleteAsync(bool includeChildren)
    {
        if (card is not { } existing || deleting)
            return;

        deleting = true;

        try
        {
            foreach (var target in includeChildren ? board.ChildrenOf(existing).Append(existing) : [existing])
                await registry.EndAsync(target.Id);

            await board.DeleteAsync(existing, includeChildren);
        }
        catch (InvalidOperationException error)
        {
            notifications.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = Strings.Task_DeleteFailed,
                Detail = error.Message,
                Duration = 20000,
            });

            deleting = false;

            return;
        }

        BackToBoard();
    }

    private void CreateWorkingDir()
    {
        try
        {
            directories.Create(form.WorkingDir);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            notifications.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = Strings.NewTask_CreateDirFailed,
                Detail = error.Message,
                Duration = 20000,
            });
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

    // A route can be reached with a stale id — a bookmark, a back button after a delete — which
    // a modal opened from a card in hand never could. Hence the not-found branch.
    //
    // The board is read synchronously first and only loaded if that misses. Coming from the board
    // the card is already in hand, so this avoids a store round-trip; more importantly it means
    // the first render already knows the card, and the page title is right immediately instead of
    // being computed as "New task" and corrected a frame later.
    protected override async Task OnParametersSetAsync()
    {
        if (CardId is not { } id)
        {
            card = null;
            missing = false;
            form = new NewTaskForm();

            return;
        }

        card = board.Card(id);

        if (card is null)
        {
            await board.LoadAsync();

            card = board.Card(id);
        }

        missing = card is null;
        form = card is { } existing ? NewTaskForm.From(existing) : new NewTaskForm();
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
            if (card is { } existing)
            {
                form.ApplyTo(existing);
                await board.UpdateAsync(existing);
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

        BackToBoard();
    }

    private void BackToBoard() => navigation.NavigateTo("/");
}
