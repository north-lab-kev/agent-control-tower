using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Resources;
using Act.App.Components.Shared;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
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
    UserSettingsService settings,
    IWorkingDirectories directories,
    IClock clock,
    DialogService dialogService,
    NavigationManager navigation,
    NotificationService notifications,
    IDesktopBridge desktop,
    IJSRuntime js) : IAsyncDisposable
{
    private NewTaskForm form = new();

    private Card? card;

    private bool missing;

    private bool saving;

    private bool picking;

    private bool deleting;

    private bool duplicating;

    // The form as it was when the page loaded or last saved. Comparing against it is the whole
    // dirty check — `NewTaskForm` is a record, so this is one `!=` rather than a flag per field.
    private NewTaskForm baseline = new();

    private IDisposable? navigationGuard;

    private DotNetObjectReference<TaskView>? owner;

    private IJSObjectReference? unsaved;

    private bool armed;

    private bool asking;

    // Set by the two exits that make the edits moot — a save that persisted them, a delete that
    // took the card away — so leaving does not then ask about them.
    private bool discarded;

    [Parameter]
    public Guid? CardId { get; set; }

    private bool IsDirty => !missing && form != baseline;

    // Past the launch boundary the form describes a run that already happened, so what defines that
    // run stops being editable — `TaskEditing` owns the split, and `NewTaskForm.ApplyTo` enforces it
    // whatever the markup does. A card that does not exist yet is never locked.
    private bool Locked => card is { } existing && !TaskEditing.CanEditLaunchInputs(existing);

    private string PageHeading => card is { } existing ? existing.Title : Strings.TaskView_NewTitle;

    private bool FolderGuardOn => settings.PreventConcurrentWorkingDir;

    // An unattended schedule with a permission mode that prompts is a task that parks at a TUI
    // prompt nobody is awake to answer, holding a concurrency slot all night — ACT never answers a
    // prompt, so nothing will move it until morning. `acceptEdits` counts as prompting: it covers
    // edits and still stops at the first `Bash` call it wants approval for.
    private bool WarnsUnattended
        => form.Schedule is not TaskSchedule.Manual
            && form.Permission is PermissionMode.Default or PermissionMode.AcceptEdits;

    // Only the agents whose switch is on — plus, always, the one this card already carries. An
    // agent turned off after a card was made must not leave that card showing an empty dropdown for
    // a value it is still holding, the same rule a retired model follows.
    private IReadOnlyList<SettingChoice<AgentType>> AgentChoices
    {
        get
        {
            var enabled = settings.EnabledAgents;

            var offered = enabled.Contains(form.Agent) ? enabled : [.. enabled, form.Agent];

            return [.. offered.Select(agent => new SettingChoice<AgentType>(agent, AgentLabel(agent)))];
        }
    }

    private static string AgentLabel(AgentType agent) => agent switch
    {
        AgentType.Codex => Strings.Agent_Codex,
        _ => Strings.Agent_ClaudeCode,
    };

    // Whatever the chosen agent declares, in its order — the same rule the model and effort lists
    // follow. Codex offers five and Claude Code six, and neither the form nor this list knows why.
    // A stored mode the agent no longer offers is kept visible for the same reason a stored model is:
    // an editing card must not show an empty dropdown for a value it is still carrying.
    private IReadOnlyList<SettingChoice<PermissionMode>> PermissionChoices
    {
        get
        {
            var offered = agents.For(form.Agent).PermissionModes;

            var modes = offered.Contains(form.Permission)
                ? offered
                : [.. offered, form.Permission];

            return [.. modes.Select(mode => new SettingChoice<PermissionMode>(mode, Label(mode)))];
        }
    }

    private static string Label(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => Strings.Permission_Default,
        PermissionMode.Plan => Strings.Permission_Plan,
        PermissionMode.AcceptEdits => Strings.Permission_AcceptEdits,
        PermissionMode.Auto => Strings.Permission_Auto,
        PermissionMode.DontAsk => Strings.Permission_DontAsk,
        PermissionMode.Bypass => Strings.Permission_Bypass,
        _ => mode.ToString(),
    };

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
            var efforts = OfferedEfforts;

            return form.Effort is { } current && !efforts.Contains(current)
                ? [.. efforts, current]
                : efforts;
        }
    }

    // "Agent default" is not "no model", so the ladder shown is the one the launch will actually
    // accept — `LaunchConfigResolver` fills the blank model in the same way before reading efforts
    // off it, and offering nothing here made the field unusable until a model was named.
    private IReadOnlyList<string> OfferedEfforts
    {
        get
        {
            var capabilities = agents.For(form.Agent);

            return capabilities.EffortsFor(form.Model ?? capabilities.DefaultModel);
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

    // The saved card is what gets copied, not the form — so unsaved edits stay behind, and the copy
    // opens on its own route where they are plainly visible rather than silently carried over.
    private async Task DuplicateAsync()
    {
        if (card is not { } existing || duplicating)
            return;

        duplicating = true;

        try
        {
            var copy = await board.DuplicateAsync(existing);

            navigation.NavigateTo($"/card/{copy.Id}/edit");
        }
        finally
        {
            duplicating = false;
        }
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

            // Archiving the card settles what happens to the edits: they go with it.
            discarded = true;
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
            form = NewTaskForm.From(settings.TaskDefaults);
            baseline = form with { };
            discarded = false;

            return;
        }

        card = board.Card(id);

        if (card is null)
        {
            await board.LoadAsync();

            card = board.Card(id);
        }

        missing = card is null;
        form = card is { } existing ? NewTaskForm.From(existing) : NewTaskForm.From(settings.TaskDefaults);
        baseline = form with { };
        discarded = false;
    }

    // Two exits, two mechanisms, because they are two different things. Every move inside ACT —
    // Cancel, back-to-board, the surface switch, the top bar, the browser's own back button —
    // arrives here as a location change and can simply be refused. The window's close button
    // never reaches Blazor at all, which is what the JS module is for.
    protected override void OnInitialized()
        => navigationGuard = navigation.RegisterLocationChangingHandler(ConfirmLeavingAsync);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            unsaved = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-unsaved.js");
            owner = DotNetObjectReference.Create(this);

            await unsaved.InvokeVoidAsync("watch", owner, desktop.IsDesktop);
        }

        if (unsaved is not { } module || armed == IsDirty)
            return;

        armed = IsDirty;

        await module.InvokeVoidAsync("arm", armed);
    }

    // Desktop only. Electron cancels a close silently when the page objects, so the page objects
    // and then hands the question here — the dialog the user would have got in a browser.
    [JSInvokable]
    public async Task OnCloseBlocked()
    {
        if (await ConfirmDiscardAsync() && unsaved is { } module)
            await module.InvokeVoidAsync("release");
    }

    public async ValueTask DisposeAsync()
    {
        navigationGuard?.Dispose();

        if (unsaved is { } module)
        {
            try
            {
                await module.InvokeVoidAsync("dispose");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        owner?.Dispose();
    }

    private async ValueTask ConfirmLeavingAsync(LocationChangingContext context)
    {
        if (discarded || !IsDirty)
            return;

        if (await ConfirmDiscardAsync())
            return;

        context.PreventNavigation();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        // One at a time: a second prompt behind the first is a way to lose the work twice over.
        if (asking)
            return false;

        asking = true;

        try
        {
            var confirmed = await dialogService.Confirm(
                Strings.Task_UnsavedConfirm,
                Strings.Task_Unsaved,
                new ConfirmOptions
                {
                    OkButtonText = Strings.Task_UnsavedDiscard,
                    CancelButtonText = Strings.Task_UnsavedStay,
                    CssClass = "act-dialog",
                });

            // Null when dismissed with the X or the overlay, which means the same as staying.
            return confirmed is true;
        }
        finally
        {
            asking = false;
        }
    }

    // `Models` and `Efforts` keep whatever the form carries in their lists on purpose, so the checks
    // here ask the agent rather than the list — otherwise a retargeted card would always look valid.
    private void OnAgentChanged(AgentType agent)
    {
        form.Agent = agent;

        var capabilities = agents.For(agent);

        if (form.Model is { } model && capabilities.Model(model) is null)
            form.Model = null;

        DropUnavailableEffort();

        // Unlike a model, a permission mode has no "agent default" to fall back to — the field is
        // required and every agent honours `Default`, so switching to an agent that does not offer the
        // chosen mode lands there rather than leaving a value the launch would reject.
        if (!capabilities.Supports(form.Permission))
            form.Permission = PermissionMode.Default;
    }

    // The ladders differ per model, not only per agent, so the same rule has to run when the model
    // changes underneath a chosen effort.
    private void OnModelChanged(string? model)
    {
        form.Model = model;

        DropUnavailableEffort();
    }

    private void DropUnavailableEffort()
    {
        if (form.Effort is { } effort && !OfferedEfforts.Contains(effort))
            form.Effort = null;
    }

    private void OnScheduleChanged(TaskSchedule schedule)
    {
        form.Schedule = schedule;

        if (!form.WantsDateTime)
            form.ScheduledFor = null;
    }

    private void OnGitActionChanged(GitAction? action)
    {
        form.SelectedGitAction = action;

        if (action is not GitAction.PullRequest)
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

            discarded = true;
        }
        finally
        {
            saving = false;
        }

        BackToBoard();
    }

    private void BackToBoard() => navigation.NavigateTo("/");

    // The folder picker is part of this page rather than a popup of its own, so Escape has to close
    // it here — leaving the page out from under an open picker is not what the key was pressed for.
    // Unsaved edits still get their question, because leaving is a navigation like any other.
    private void OnEscape()
    {
        if (picking)
        {
            picking = false;

            return;
        }

        BackToBoard();
    }
}
