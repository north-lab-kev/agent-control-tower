using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Resources;
using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Radzen;

namespace Act.App.Components.Pages;

// The full-screen terminal for one card. It attaches to a session the registry already holds
// rather than owning one, so navigating away and back re-attaches to the same live process and
// replays its scrollback instead of starting anything.
public partial class SessionView(
    BoardState board,
    SessionRegistry registry,
    SessionLauncher launcher,
    TerminalGeometry geometry,
    CardCompleter completer,
    CardReopener reopener,
    IEnumerable<IAgentAdapter> adapters,
    IAttachmentStore attachments,
    AttachmentOpener opener,
    NavigationManager navigation,
    NotificationService notifications,
    IDesktopBridge desktop,
    IJSRuntime js,
    ILogger<SessionView> log) : IAsyncDisposable
{
    private readonly string terminalId = $"act-term-{Guid.NewGuid():N}";

    private readonly string dropRootId = $"act-drop-{Guid.NewGuid():N}";

    private readonly string dropInputId = $"act-file-{Guid.NewGuid():N}";

    // One id, not one per attachment: only the hovered row renders a preview, so there is never a
    // second element wearing it.
    private readonly string previewId = $"act-preview-{Guid.NewGuid():N}";

    private TaskAttachment? previewing;

    private DotNetObjectReference<SessionView>? owner;

    private IJSObjectReference? module;

    private IJSObjectReference? attach;

    private bool dragging;

    private IAgentSession? session;

    private Card? card;

    private bool live;

    private bool launching;

    private bool completing;

    private bool restarting;


    [Parameter]
    public Guid CardId { get; set; }

    private string TerminalId => terminalId;

    private string DropRootId => dropRootId;

    private string DropInputId => dropInputId;

    private string PreviewId => previewId;

    // Only what can actually be shown. A log or a PDF has no thumbnail, and neither does a file that is
    // no longer on disk — an empty frame under the cursor reads as a bug rather than as "it is gone".
    private bool Previews(TaskAttachment attachment)
        => ReferenceEquals(previewing, attachment) && attachment.IsImage && !Missing(attachment);

    // The card keeps a *name*; the bytes can go without it. Asked at render time rather than stored, so
    // the row cannot claim a file that is not there — see the same note on `TaskView`.
    private bool Missing(TaskAttachment attachment)
        => attachments.ResolveInside(CardId, attachment.FileName) is null;

    private static string RowIcon(TaskAttachment attachment, bool missing) => missing
        ? "broken_image"
        : attachment.IsImage ? "image" : "description";

    private string PreviewUrl(TaskAttachment attachment)
        => AttachmentEndpointExtensions.UrlFor(CardId, attachment);

    // Names the file as well as the action, so it doubles as the tooltip a truncated name needs — the
    // rail is 15rem wide and most names are cut short in it. Says the file is gone rather than offering
    // to open nothing.
    private static string RowLabel(TaskAttachment attachment, bool missing) => missing
        ? Text.Format(Strings.NewTask_Attachments_GoneDetail, attachment.FileName)
        : Text.Format(Strings.NewTask_Attachments_Open, attachment.FileName);

    // The only way to look at an attachment the hover preview cannot show. Works on an archived card
    // too: the files outlive the session, and opening one changes nothing.
    private async Task OpenAttachmentAsync(TaskAttachment attachment)
    {
        var result = await opener.OpenAsync(CardId, attachment);

        if (result.Outcome is AttachmentOpenOutcome.Opened)
            return;

        notifications.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Warning,
            Summary = result.Outcome is AttachmentOpenOutcome.Missing
                ? Strings.NewTask_Attachments_Gone
                : Strings.NewTask_Attachments_OpenFailed,
            Detail = result.Outcome is AttachmentOpenOutcome.Missing
                ? Text.Format(Strings.NewTask_Attachments_GoneDetail, attachment.FileName)
                : result.Detail ?? Text.Format(
                    Strings.NewTask_Attachments_OpenFailedDetail,
                    attachment.FileName),
            Duration = 8000,
        });
    }

    private TerminalSize Geometry { get; set; } = TerminalSize.Default;

    // An archived card has no terminal: nothing is attached, nothing is bound and nothing is
    // resumed, so this face is the empty statement of that rather than a black pane. The rules
    // refuse every action independently — see `TaskEditing` and `SessionRestore` — and this is what
    // keeps the page from spawning one on its way to being told no.
    private bool Archived => card is { IsOnBoard: false };

    private bool CanLaunch => card is { } existing && launcher.CanLaunch(existing);

    // The review happens in front of this terminal, so this is where the sign-off belongs.
    private bool CanComplete => card is { } existing && completer.CanComplete(existing);

    // And its undo, in the same place: reading the transcript of a signed-off card is exactly when
    // the user finds the thing they still wanted to say.
    private bool CanReopen => card is { } existing && reopener.CanReopen(existing);

    // Offered wherever there is a session to resume, live or not — a card whose process is already
    // gone is restored on open, and asking for that again is a reasonable thing to want when the
    // screen looks wrong.
    private bool CanRestart => card is { } existing && launcher.CanRestart(existing);

    // Null until the session id is known, which for Codex is a little after launch — and null
    // forever for an agent with no desktop app, so the action simply does not appear.
    private string? HandoffUrl
    {
        get
        {
            if (card?.SessionId is not { } sessionId)
                return null;

            var adapter = adapters.FirstOrDefault(candidate => candidate.Agent == card.AgentType);

            return adapter?.DesktopHandoffUrl(sessionId, card.WorkingDir);
        }
    }

    // Synchronous first, load only on a miss — see the same note on TaskView: it keeps the page
    // title correct on the first render instead of a frame late.
    protected override async Task OnInitializedAsync()
    {
        card = board.Card(CardId);

        if (card is null)
        {
            await board.LoadAsync();

            card = board.Card(CardId);
        }

        registry.Changed += OnRegistryChanged;

        // The card moves under this view while the user watches it: a turn ending is what puts the
        // sign-off within reach, and the badge and the rail's numbers are only true if they follow.
        board.Changed += OnBoardChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (card is null)
            return;

        if (firstRender)
        {
            owner = DotNetObjectReference.Create(this);

            // Loaded even for an **archived** card, which renders no terminal at all: the module also
            // owns `place`, and the rail lists this card's attachments either way — the files outlive
            // the session, since only a purge removes them. Gating the import on the terminal being
            // there is precisely the mistake the task form made with `Locked`, where a completed card
            // rendered a preview nothing ever positioned.
            attach = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-attach.js");

            if (!Archived)
            {
                module = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-terminal.js");

                // Bound on the terminal frame, not the page: the rail's buttons are not somewhere a
                // file means anything, and a paste is only claimed when the clipboard carries files —
                // so xterm keeps handling a text paste exactly as it did.
                await attach.InvokeVoidAsync("watch", dropRootId, dropInputId, owner);

                await AttachAsync();
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
    }

    // The one write ACT authors into a live terminal, and it is deliberately the least it can be: the
    // file's own path, quoted when it has a space in it, typed where the cursor already is. No submit
    // key — the user reads what landed and presses Enter, exactly as they would after dragging a file
    // onto any other terminal. See `IAgentTerminal`.
    private async Task OnAttachmentsDroppedAsync(InputFileChangeEventArgs args)
    {
        dragging = false;

        if (card is not { } existing)
            return;

        if (session is not { } running)
        {
            notifications.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Warning,
                Summary = Strings.Session_AttachNoSession,
                Detail = Strings.Session_AttachNoSessionDetail,
                Duration = 5000,
            });

            return;
        }

        List<string> quoted = [];

        foreach (var file in args.GetMultipleFiles(TaskAttachment.MaxPerTask))
        {
            if (file.Size > TaskAttachment.MaxLength)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = Strings.NewTask_Attachments_TooLarge,
                    Detail = Text.Format(
                        Strings.NewTask_Attachments_TooLargeDetail,
                        file.Name,
                        TaskLabels.FileSize(TaskAttachment.MaxLength)),
                    Duration = 5000,
                });

                continue;
            }

            try
            {
                await using var content = file.OpenReadStream(TaskAttachment.MaxLength);

                // Into the same folder the launch already granted the CLI access to, and *not* onto
                // the card: the prompt is the opening instruction and stays verbatim, so a file
                // handed over mid-session belongs to the conversation rather than to the task record.
                var saved = await attachments.SaveAsync(existing.Id, file.Name, content);

                quoted.Add(Quoted(attachments.PathFor(existing.Id, saved)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or JSException or TaskCanceledException)
            {
                log.LogError(error, "Attaching {FileName} to the live session failed.", file.Name);

                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = Strings.Session_AttachFailed,
                    Detail = error.Message,
                    Duration = 5000,
                });
            }
        }

        if (quoted.Count > 0)
            await running.Terminal.WriteAsync(string.Join(" ", quoted) + " ");
    }

    // Double quotes and a trailing space, which is what a terminal emulator does with a dragged file
    // and what both TUIs read back as one argument. ACT's own directory names cannot contain a quote
    // — `AttachmentStore` strips every invalid filename character — so there is nothing to escape
    // inside the quotes.
    private static string Quoted(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    [JSInvokable]
    public void OnDragActive(bool active) => InvokeAsync(() =>
    {
        if (dragging == active)
            return;

        dragging = active;

        StateHasChanged();
    });

    [JSInvokable]
    public async Task OnData(string data)
    {
        if (session is { } running)
            await running.Terminal.WriteAsync(data);
    }

    [JSInvokable]
    public void OnResize(int cols, int rows)
    {
        Geometry = new TerminalSize(cols, rows);

        geometry.Report(Geometry);

        session?.Terminal.Resize(cols, rows);
    }

    public async ValueTask DisposeAsync()
    {
        registry.Changed -= OnRegistryChanged;
        board.Changed -= OnBoardChanged;

        // Only the *view* goes away here. The session keeps running, which is the whole point
        // of the registry owning it — walking back into the card finds it still working.
        if (session is { } running)
            running.Terminal.Output -= WriteToTerminalAsync;

        if (module is { } loaded)
        {
            try
            {
                await loaded.InvokeVoidAsync("dispose", terminalId);
                await loaded.DisposeAsync();
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

    private async Task AttachAsync()
    {
        if (module is not { } loaded || owner is null)
            return;

        var size = await loaded.InvokeAsync<TerminalMeasurement?>("attach", terminalId, owner);

        Geometry = size is null ? geometry.Last : new TerminalSize(size.Cols, size.Rows);

        geometry.Report(Geometry);

        BindSession();

        // Attach only, never on a later registry change: a card that lost its process — to the CLI's
        // own exit, to an ACT restart the startup restore did not cover, to the sign-off that ended
        // it — gets its terminal back by being opened. On a later change it must not, or the restart
        // below would race this into resuming the session it just ended.
        if (session is null)
            await RestoreAsync();

        StateHasChanged();
    }

    private async Task RestoreAsync()
    {
        if (card is not { } existing || !SessionRestore.IsResumable(existing) || launching)
            return;

        launching = true;

        try
        {
            var result = await launcher.RestoreAsync(existing, Geometry);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = Strings.Session_RestoreFailed,
                    Detail = message,
                    Duration = 5000,
                });
            }

            card = board.Card(CardId);

            BindSession();
        }
        finally
        {
            launching = false;
        }
    }

    private void BindSession()
    {
        var found = registry.For(CardId);
        if (ReferenceEquals(found, session))
            return;

        if (session is { } previous)
            previous.Terminal.Output -= WriteToTerminalAsync;

        session = found;
        live = session is not null;

        if (session is not { } attached)
            return;

        attached.Terminal.Output += WriteToTerminalAsync;

        // Replay before subscribing takes effect for new chunks, so the screen looks the way it
        // did when the view was last open rather than blank until the agent next paints.
        _ = WriteToTerminalAsync(attached.Terminal.Backlog);

        // A session launched from the board has never seen this view, so its pty is still at the
        // default geometry; the replayed backlog was drawn for that size. Sizing it to the xterm
        // makes the CLI repaint at the size it is actually being shown at.
        attached.Terminal.Resize(Geometry.Cols, Geometry.Rows);
    }

    // A failed launch reports through the notification host rather than into the side rail: the
    // rail is 15rem wide and a launch failure is usually a path or a command line, which it cannot
    // show without overflowing. It is also transient news, not part of the card's description.
    private async Task LaunchAsync()
    {
        if (card is null || launching)
            return;

        launching = true;

        try
        {
            var result = await launcher.LaunchAsync(card, Geometry);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = result.Waiting ? NotificationSeverity.Warning : NotificationSeverity.Error,
                    Summary = result.Waiting ? Strings.Session_NotLaunched : Strings.Session_LaunchFailed,
                    Detail = message,
                    Duration = 5000,
                });
            }

            card = board.Card(CardId);

            BindSession();
        }
        finally
        {
            launching = false;
        }
    }

    // Signing off ends the session, which leaves this view showing an empty pane for work that is
    // done — so it returns to the board, where the card now sits in Completed.
    private async Task CompleteAsync()
    {
        if (card is not { } existing || completing)
            return;

        completing = true;

        try
        {
            if (await completer.CompleteAsync(existing))
                navigation.NavigateTo("/");
        }
        finally
        {
            completing = false;
        }
    }

    // Unlike the sign-off this stays on the page: the terminal it reopened into is the reason the
    // user took the completion back, and the card is now in Your turn where the next step is said.
    private async Task ReopenAsync()
    {
        if (card is not { } existing || completing)
            return;

        completing = true;

        try
        {
            await reopener.ReopenAsync(existing);

            card = board.Card(CardId);
        }
        finally
        {
            completing = false;
        }
    }

    // A fresh terminal on the same session. The old pty goes first — two ptys on one session id is
    // two CLIs writing one transcript — and the xterm is reset before rebinding, because the new
    // session replays its own backlog and the dead one's output above it would read as one screen.
    private async Task RestartTerminalAsync()
    {
        if (card is not { } existing || restarting)
            return;

        restarting = true;

        try
        {
            var result = await launcher.RestartAsync(existing, Geometry);

            if (result.Message is { } message)
            {
                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = Strings.Session_RestartFailed,
                    Detail = message,
                    Duration = 5000,
                });
            }

            if (module is { } loaded)
                await loaded.InvokeVoidAsync("clear", terminalId);

            card = board.Card(CardId);

            BindSession();
        }
        finally
        {
            restarting = false;
        }
    }

    private Task OpenDesktopAsync(string url) => desktop.OpenExternalAsync(url);

    // The board, or the archive for a card that is off it — see `CardExit`.
    private void Back() => navigation.NavigateTo(CardExit.Route(card));

    private async Task WriteToTerminalAsync(string text)
    {
        if (module is not { } loaded || text.Length == 0)
            return;

        try
        {
            // `async` and awaited inside, because `InvokeVoidAsync` returns a `ValueTask` and a
            // lambda handing one straight back binds to the `Action` overload — the interop call
            // would be dispatched and forgotten, the catches below would never see a disconnected
            // circuit, and the pty's flush loop would lose the backpressure it gets from awaiting
            // this handler.
            await InvokeAsync(async () => await loaded.InvokeVoidAsync("write", terminalId, text));
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnRegistryChanged() => InvokeAsync(() =>
    {
        BindSession();
        StateHasChanged();
    });

    // The store's copy is a fresh object on every write, so the view has to take the new one rather
    // than hold the instance it was initialized with.
    private void OnBoardChanged() => InvokeAsync(() =>
    {
        card = board.Card(CardId);

        StateHasChanged();
    });

    // What `attach` measures the xterm to be. Distinct from `TerminalGeometry`, the service that
    // remembers it for the next headless launch — this one is the wire shape.
    private sealed record TerminalMeasurement(int Cols, int Rows);
}
