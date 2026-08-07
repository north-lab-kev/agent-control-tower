using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Components.Shared;
using Act.App.Desktop;
using Act.App.Notifications;
using Act.App.Resources;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.FileSystem;
using Act.Infrastructure.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
    TaskTitles titles,
    UserSettingsService settings,
    IWorkingDirectories directories,
    IAttachmentStore attachments,
    AttachmentOpener opener,
    IClock clock,
    DialogService dialogService,
    NavigationManager navigation,
    NotificationService notifications,
    IDesktopBridge desktop,
    IJSRuntime js,
    ILogger<TaskView> log) : IAsyncDisposable
{
    private NewTaskForm form = new();

    private Card? card;

    private bool missing;

    private bool saving;

    private bool picking;

    private bool deleting;

    private bool duplicating;

    private bool titling;

    private bool templating;

    private bool dragging;

    private TaskAttachment? previewing;

    // The names the form loaded or last saved with. The dirty check cannot come off `NewTaskForm`'s
    // own equality here: the record holds a `List<T>`, which compares by reference, so `form with { }`
    // hands the baseline the very list the page then mutates.
    private List<string> attachmentBaseline = [];

    private readonly string dropRootId = $"act-drop-{Guid.NewGuid():N}";

    private readonly string dropInputId = $"act-file-{Guid.NewGuid():N}";

    // One id, not one per attachment: only the hovered row renders a preview, so there is never a
    // second element wearing it.
    private readonly string previewId = $"act-preview-{Guid.NewGuid():N}";

    private IJSObjectReference? attach;

    // Titling outlives no navigation: the answer is for a form that is still on screen, and a CLI
    // still thinking about a page the user has left is work nobody will read.
    private CancellationTokenSource? titleRun;

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

    // Which saved starting point a create begins from. A query parameter rather than a route of its
    // own: it narrows `/card/new` rather than naming a different destination, and an id that no
    // longer exists falls back to the default instead of 404ing a page whose job is to make a task.
    [SupplyParameterFromQuery(Name = "template")]
    private Guid? TemplateId { get; set; }

    private bool IsDirty
        => !missing && (form != baseline || !AttachmentNames.SequenceEqual(attachmentBaseline));

    private IEnumerable<string> AttachmentNames => form.Attachments.Select(a => a.FileName);

    private string DropZoneText => dragging
        ? Strings.NewTask_Attachments_DropActive
        : Strings.NewTask_Attachments_Drop;

    // The row logic is `AttachmentRows`', shared with the session rail; what stays here only binds
    // this form's card and hover state into it.
    private bool Previews(TaskAttachment attachment)
        => AttachmentRows.Previews(previewing, attachment, Missing(attachment));

    private bool Missing(TaskAttachment attachment)
        => AttachmentRows.Missing(attachments, form.CardId, attachment);

    private string PreviewUrl(TaskAttachment attachment)
        => AttachmentRows.PreviewUrl(form.CardId, attachment);

    // Offered on a locked card too: opening a file changes nothing about the run.
    private Task OpenAttachmentAsync(TaskAttachment attachment)
        => AttachmentRows.OpenAsync(opener, notifications, form.CardId, attachment);

    // An archived card is a record: it opens, because reading it is why the archive keeps it, and
    // nothing on it moves. Restoring is the way back to an editable task, and it lives on the
    // archive page rather than here — this surface stays entirely read-only.
    private bool ReadOnly => card is { } existing && !TaskEditing.CanEdit(existing);

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

            return [.. offered.Select(agent => new SettingChoice<AgentType>(agent, TaskLabels.Agent(agent)))];
        }
    }

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

            return [.. modes.Select(mode => new SettingChoice<PermissionMode>(mode, TaskLabels.Permission(mode)))];
        }
    }

    private static SettingChoice<TaskSchedule>[] ScheduleChoices => TaskLabels.Schedules(withDateTime: true);

    private static SettingChoice<GitAction?>[] GitChoices => TaskLabels.GitActions();

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

    // Asked once per render — the markup hoists it into a local — because a check is a real syscall
    // and the section reads it three ways. Deliberately not cached across renders: the create-folder
    // button fixes the disk without changing the typed text, and the next render's fresh check is
    // what clears the warning.
    private PathCheck Dir => directories.Check(form.WorkingDir);

    // Malformed blocks the save; merely missing does not, because a directory you are about to
    // create is a perfectly reasonable thing to save a task against. The validator calls this at
    // validation time, outside any render, so it takes its own fresh check.
    private bool WorkingDirValid => string.IsNullOrWhiteSpace(form.WorkingDir) || Dir.WellFormed;

    private static string WorkingDirError(PathCheck dir) => dir.Error switch
    {
        PathError.NotAbsolute => Strings.NewTask_DirNotAbsolute,
        _ => Strings.NewTask_DirMalformed,
    };

    private static bool WorkingDirMissing(PathCheck dir) => dir.WellFormed && !dir.Exists;

    // Shows the resolved path too, because the typed one and the real one differ whenever a `~`
    // or a forward slash is involved, and that difference is exactly what confuses people.
    private string WorkingDirMissingText(PathCheck dir)
        => dir.Resolved == form.WorkingDir
            ? Strings.NewTask_DirMissing
            : Text.Format(Strings.NewTask_DirMissingResolved, dir.Resolved);

    // The picked path is absolute and real, so it replaces whatever was typed rather than being
    // merged with it, and the picker closes — it has done its job.
    private void OnFolderPicked(string path)
    {
        form.WorkingDir = path;
        picking = false;
    }

    // Written to disk on add rather than on save, which is what lets a 25 MB screenshot leave the
    // server's memory immediately instead of being held per circuit until the user commits. The
    // directory is named after the id the form minted, so a create stages into the folder the saved
    // card will own and there is nothing to move.
    private async Task OnAttachmentsPickedAsync(InputFileChangeEventArgs args)
    {
        // Refused whole rather than truncated. Keeping the first few of a dropped selection is the
        // kind of partial success nobody notices until the agent asks about a file that never came.
        if (args.FileCount > TaskAttachment.MaxPerTask - form.Attachments.Count)
        {
            notifications.Toast(
                NotificationSeverity.Warning,
                Strings.NewTask_Attachments_TooMany,
                Text.Format(Strings.NewTask_Attachments_TooManyDetail, TaskAttachment.MaxPerTask));

            return;
        }

        foreach (var file in args.GetMultipleFiles(TaskAttachment.MaxPerTask))
        {
            // Checked here as well as by `OpenReadStream`, so an oversized file is named in the
            // message instead of arriving as an `IOException` halfway through the copy.
            if (file.Size > TaskAttachment.MaxLength)
            {
                notifications.Toast(
                    NotificationSeverity.Warning,
                    Strings.NewTask_Attachments_TooLarge,
                    Text.Format(
                        Strings.NewTask_Attachments_TooLargeDetail,
                        file.Name,
                        TaskLabels.FileSize(TaskAttachment.MaxLength)));

                continue;
            }

            try
            {
                await using var content = file.OpenReadStream(TaskAttachment.MaxLength);

                form.Attachments.Add(await attachments.SaveAsync(form.CardId, file.Name, content));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or JSException or TaskCanceledException)
            {
                log.LogError(error, "Attaching {FileName} to the task failed.", file.Name);

                notifications.Toast(NotificationSeverity.Error, Strings.NewTask_Attachments_Failed, error.Message);
            }
        }
    }

    // In memory only. The bytes go when the form commits — see `IAttachmentStore.Prune` — because a
    // removal the user then abandons must not leave the stored card naming a file that is gone.
    private void RemoveAttachment(TaskAttachment attachment) => form.Attachments.Remove(attachment);

    [JSInvokable]
    public void OnDragActive(bool active) => InvokeAsync(() =>
    {
        if (dragging == active)
            return;

        dragging = active;

        StateHasChanged();
    });

    // Only the follow-ups that are still live. Ones already archived are not a decision the user
    // needs to make again.
    private IReadOnlyList<Card> LiveFollowUps => card is { } existing
        ? [.. board.ChildrenOf(existing).Where(child => !child.IsDeleted)]
        : [];

    // Straight to the archive when nothing hangs off the card; otherwise ask what should happen to
    // the follow-ups, which is a genuine choice rather than an "are you sure".
    private async Task StartDeleteAsync()
    {
        if (card is not { } existing || ReadOnly)
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

    // The form rather than the saved card, unlike Duplicate: a template is not a run, so it is the
    // shape of what is on screen — which is also what lets one be captured on a task that has never
    // been saved. The name is asked for because nothing else on the form describes a *kind* of task;
    // the title names this one.
    private async Task SaveAsTemplateAsync()
    {
        if (templating)
            return;

        templating = true;

        try
        {
            var name = await dialogService.OpenAsync<TemplateNameDialog>(
                Strings.Task_SaveAsTemplate,
                new Dictionary<string, object?>
                {
                    [nameof(TemplateNameDialog.Suggested)] = form.Title.Trim(),
                },
                new DialogOptions { Width = "460px", CloseDialogOnOverlayClick = true, CssClass = "act-dialog" });

            // Null when dismissed with the X or the overlay, which means the same as Cancel.
            if (name is not string named || string.IsNullOrWhiteSpace(named))
                return;

            settings.SaveTemplate(form.ToTemplate(named));

            notifications.Toast(
                NotificationSeverity.Success,
                Strings.Task_SaveAsTemplate_Saved,
                Text.Format(Strings.Task_SaveAsTemplate_SavedDetail, named.Trim()));
        }
        finally
        {
            templating = false;
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
            using (log.BeginTaskScope(existing.Number, existing.SessionId))
                log.LogError(error, "Archiving the task failed (with children: {IncludeChildren}).", includeChildren);

            notifications.Toast(NotificationSeverity.Error, Strings.Task_DeleteFailed, error.Message);

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
            log.LogError(error, "Creating the working directory {WorkingDir} failed.", form.WorkingDir);

            notifications.Toast(NotificationSeverity.Error, Strings.NewTask_CreateDirFailed, error.Message);
        }
    }

    private string PermissionHint => TaskLabels.PermissionHint(form.Permission);

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
            form = NewTaskForm.From(settings.TemplateOrDefault(TemplateId));
            Rebase();
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
        form = card is { } existing
            ? NewTaskForm.From(existing)
            : NewTaskForm.From(settings.TemplateOrDefault(TemplateId));
        Rebase();
        discarded = false;
    }

    // The two snapshots the dirty check compares against, taken together so they cannot drift.
    private void Rebase()
    {
        baseline = form with { };
        attachmentBaseline = [.. AttachmentNames];
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

            // The module is loaded for **every** card that exists, locked or not, because it does two
            // jobs and only one of them is about adding files: `place` positions the hover preview,
            // which a launched card wants as much as a draft does. Gating the import on `Locked` — as
            // this did at first — left a completed card rendering a preview that was never placed and
            // so never became visible.
            if (!missing)
            {
                attach = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-attach.js");

                // Only the *input* half is gated: drop and paste feed a file input a locked card does
                // not render. Bound on the whole page rather than on the panel, because a file dragged
                // at a form is aimed at the prompt as often as at the box that collects it, and the
                // highlight tells the user where it will land either way.
                if (!Locked)
                    await attach.InvokeVoidAsync("watch", dropRootId, dropInputId, owner);
            }
        }

        // After the render that added it, because the box only has a position once it is in the
        // document — and it stays `visibility: hidden` until this lands, so it never paints at the
        // viewport's top-left corner on the way past.
        if (previewing is not null && attach is { } placing)
        {
            try
            {
                await placing.InvokeVoidAsync("place", previewId);
            }
            catch (JSDisconnectedException)
            {
            }
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
        if (!await ConfirmDiscardAsync())
            return;

        DiscardAttachments();

        if (unsaved is { } module)
            await module.InvokeVoidAsync("release");
    }

    // The other half of writing files on add: a discard has to undo them, or a task the user walked
    // away from leaves bytes nothing on the board names. A card that was never saved loses the whole
    // directory; an edit is rolled back to the names it arrived with, which is also what un-does an
    // attachment removed and then abandoned.
    private void DiscardAttachments()
    {
        if (card is null)
            attachments.Clear(form.CardId);
        else
            PruneAttachments(attachmentBaseline);
    }

    // The one place that deletes an existing card's files, so it is the one place worth guarding and the
    // one place worth logging. Two reasons a prune is refused:
    //
    //   * **The card is locked.** Its attachments cannot have changed, so there is nothing to reconcile
    //     and everything to lose.
    //   * **The card still names files the prune would not keep.** That is not a removal the user made;
    //     it is the form's copy of the list disagreeing with the card's, and the card is the record.
    //     Belt and braces behind the first guard, because this is a delete of the user's own data.
    private void PruneAttachments(IEnumerable<string> keep)
    {
        if (Locked)
            return;

        var kept = keep.ToList();

        if (card is { } existing
            && existing.Attachments.Any(attachment => !kept.Contains(attachment.FileName)))
        {
            using (log.BeginTaskScope(existing.Number, existing.SessionId))
                log.LogWarning(
                    "Not pruning attachments: the card still names {Named} file(s) and the form would "
                        + "keep only {Kept}.",
                    existing.Attachments.Count,
                    kept.Count);

            return;
        }

        attachments.Prune(form.CardId, kept);
    }

    public async ValueTask DisposeAsync()
    {
        navigationGuard?.Dispose();

        titleRun?.Cancel();
        titleRun?.Dispose();

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

        if (attach is { } dropping)
        {
            try
            {
                await dropping.InvokeVoidAsync("dispose", dropRootId);
                await dropping.DisposeAsync();
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
        {
            DiscardAttachments();

            return;
        }

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

    // A title is required; typing it is not. Blank passes only while the prompt can stand in — which
    // is the same condition `NewTaskForm.ApplyTo` falls back on, so what the field permits and what
    // the card ends up with cannot drift apart. With both blank the message names the title, because
    // that is the field the user is looking at.
    private bool TitleSatisfied
        => !string.IsNullOrWhiteSpace(form.Title) || !string.IsNullOrWhiteSpace(form.Prompt);

    // Explicit intent, so it overwrites: a user who clicks this while a title is already there is
    // asking for a different one, and refusing would leave the button doing nothing on the very card
    // where it was pressed. Their own text is one Escape-free `Ctrl+Z` away in the box either way.
    private bool CanGenerateTitle
        => !titling && !saving && !ReadOnly && !string.IsNullOrWhiteSpace(form.Prompt);

    private async Task GenerateTitleAsync()
    {
        if (!CanGenerateTitle)
            return;

        var suggestion = await TitleAsync();

        // The one place a failure is worth a word. The user asked a question here, and a box that
        // fills with the prompt's opening words with no explanation looks like a bug rather than a
        // fallback.
        if (suggestion is { Generated: false })
            notifications.Toast(
                NotificationSeverity.Warning,
                Strings.NewTask_GenerateTitleFailed,
                Strings.NewTask_GenerateTitleFallback);

        if (suggestion is { } result)
            form.Title = result.Title;
    }

    private async Task<TaskTitles.Result?> TitleAsync()
    {
        titling = true;

        titleRun?.Dispose();
        titleRun = new CancellationTokenSource();

        try
        {
            return await titles.SuggestAsync(form.Prompt, form.Agent, titleRun.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            titling = false;
        }
    }

    // The button is gone on an archived card, and the check is here as well because a form still
    // submits on Enter in any of its boxes.
    private async Task OnSubmitAsync(NewTaskForm _)
    {
        if (saving || ReadOnly)
            return;

        saving = true;

        try
        {
            // Silent here, unlike the button: the user was saving, not asking for a title, and a
            // toast about how the title was arrived at is an interruption they did not invite.
            if (string.IsNullOrWhiteSpace(form.Title) && await TitleAsync() is { } suggestion)
                form.Title = suggestion.Title;

            if (card is { } existing)
            {
                form.ApplyTo(existing);
                await board.UpdateAsync(existing);
            }
            else
            {
                await board.CreateAsync(form.ToCard(clock.Now));
            }

            // After the write, so the card and the directory agree from here on: an attachment the
            // user removed on this visit is what loses its bytes, and only once the removal is real.
            //
            // **Never on a locked card.** Past the launch boundary the attachment list is immutable —
            // `ApplyTo` will not write it and the markup offers no way to remove one — so there is
            // nothing here to reconcile and pruning is pure downside: it is a delete driven by the
            // *form's* copy of the list against a card that owns the real one. A save that only changed
            // the title would have taken the files with it if that copy were ever short. This is the
            // one plausible route by which a launched task lost its attachment on 2026-08-05.
            PruneAttachments(AttachmentNames);

            discarded = true;
        }
        finally
        {
            saving = false;
        }

        BackToBoard();
    }

    // Leaving the page returns to the list the card is on — see `CardExit`. The two exits above
    // keep the board on purpose: a save and an archive are both moves *of* the card, made from the
    // board, and the result of each is what the board now shows.
    private void Back() => navigation.NavigateTo(CardExit.Route(card));

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

        Back();
    }
}
