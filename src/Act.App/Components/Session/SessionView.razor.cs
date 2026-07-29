using Act.App.Cards;
using Act.App.Resources;
using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Act.App.Components.Session;

// The full-screen terminal for one card. It attaches to a session the registry already holds
// rather than owning one, so navigating away and back re-attaches to the same live process and
// replays its scrollback instead of starting anything.
public partial class SessionView(
    BoardState board,
    SessionRegistry registry,
    SessionLauncher launcher,
    IEnumerable<IAgentAdapter> adapters,
    NavigationManager navigation,
    IJSRuntime js) : IAsyncDisposable
{
    private readonly string terminalId = $"act-term-{Guid.NewGuid():N}";

    private DotNetObjectReference<SessionView>? owner;

    private IJSObjectReference? module;

    private IAgentSession? session;

    private Card? card;

    private bool live;

    private bool launching;

    private string? launchError;

    [Parameter]
    public Guid CardId { get; set; }

    private string TerminalId => terminalId;

    private TerminalSize Geometry { get; set; } = TerminalSize.Default;

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

    protected override async Task OnInitializedAsync()
    {
        await board.LoadAsync();

        card = board.Card(CardId);
        registry.Changed += OnRegistryChanged;
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

        StateHasChanged();
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
    }

    private async Task LaunchAsync()
    {
        if (card is null || launching)
            return;

        launching = true;
        launchError = null;

        try
        {
            var result = await launcher.LaunchAsync(card, Geometry);

            launchError = result.Message;
            card = board.Card(CardId);

            BindSession();
        }
        finally
        {
            launching = false;
        }
    }

    private async Task KillAsync()
    {
        await registry.EndAsync(CardId);

        session = null;
        live = false;
    }

    private Task OpenDesktopAsync(string url) => js.InvokeVoidAsync("open", url, "_blank").AsTask();

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

    private static string? BadgeClass(Badge badge) => badge switch
    {
        Badge.Running or Badge.Compacting => "b-run",
        Badge.NeedsPermission or Badge.NeedsAnswer => "b-wait",
        Badge.Error or Badge.Killed => "b-err",
        Badge.Stale => "b-stale",
        Badge.Idle => "b-review",
        _ => null,
    };

    private static string? BadgeText(Badge badge) => badge switch
    {
        Badge.Running => Strings.Badge_Running,
        Badge.Compacting => Strings.Badge_Compacting,
        Badge.NeedsPermission => Strings.Badge_NeedsPermission,
        Badge.NeedsAnswer => Strings.Badge_NeedsAnswer,
        Badge.Error => Strings.Badge_Error,
        Badge.Killed => Strings.Badge_Killed,
        Badge.Stale => Strings.Badge_Stale,
        Badge.Idle => Strings.Badge_IdleReady,
        _ => null,
    };

    private sealed record TerminalGeometry(int Cols, int Rows);
}
