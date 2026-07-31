using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Resources;
using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Microsoft.AspNetCore.Components;
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
    CardCompleter completer,
    IEnumerable<IAgentAdapter> adapters,
    NavigationManager navigation,
    NotificationService notifications,
    IDesktopBridge desktop,
    IJSRuntime js) : IAsyncDisposable
{
    private readonly string terminalId = $"act-term-{Guid.NewGuid():N}";

    private DotNetObjectReference<SessionView>? owner;

    private IJSObjectReference? module;

    private IAgentSession? session;

    private Card? card;

    private bool live;

    private bool launching;

    private bool completing;


    [Parameter]
    public Guid CardId { get; set; }

    private string TerminalId => terminalId;

    private TerminalSize Geometry { get; set; } = TerminalSize.Default;

    private bool CanLaunch => card is { } existing && launcher.CanLaunch(existing);

    // The review happens in front of this terminal, so this is where the sign-off belongs.
    private bool CanComplete => card is { } existing && completer.CanComplete(existing);

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
        if (!firstRender || card is null)
            return;

        module = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-terminal.js");
        owner = DotNetObjectReference.Create(this);

        await AttachAsync();
    }

    [JSInvokable]
    public async Task OnData(string data)
    {
        if (session is { } running)
            await running.Terminal.WriteAsync(data);
    }

    [JSInvokable]
    public void OnResize(int cols, int rows) => session?.Terminal.Resize(cols, rows);

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

        owner?.Dispose();
    }

    private async Task AttachAsync()
    {
        if (module is not { } loaded || owner is null)
            return;

        var size = await loaded.InvokeAsync<TerminalGeometry?>("attach", terminalId, owner);

        Geometry = size is null ? TerminalSize.Default : new TerminalSize(size.Cols, size.Rows);

        BindSession();

        // Attach only, never on a later registry change: a card that lost its process — to a kill,
        // to the CLI's own exit, to a restart the startup restore did not cover — gets its terminal
        // back by being opened, but a kill from this very view has to stay killed.
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
                    Duration = 20000,
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
                    Severity = NotificationSeverity.Error,
                    Summary = Strings.Session_LaunchFailed,
                    Detail = message,
                    Duration = 20000,
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

    private async Task KillAsync()
    {
        await registry.EndAsync(CardId);

        session = null;
        live = false;
    }

    private Task OpenDesktopAsync(string url) => desktop.OpenExternalAsync(url);

    private void BackToBoard() => navigation.NavigateTo("/");

    private async Task WriteToTerminalAsync(string text)
    {
        if (module is not { } loaded || text.Length == 0)
            return;

        try
        {
            await InvokeAsync(() => loaded.InvokeVoidAsync("write", terminalId, text));
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

    private sealed record TerminalGeometry(int Cols, int Rows);
}
