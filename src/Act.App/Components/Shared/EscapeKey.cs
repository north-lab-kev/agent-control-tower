using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Act.App.Components.Shared;

// A keyboard binding rather than a control, so it renders nothing: a page drops it in and says what
// Escape means there. What the key does is the page's business — usually the same thing as its back
// arrow, but a page with something open of its own can close that first.
public sealed class EscapeKey(IJSRuntime js) : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<EscapeKey>? owner;

    private IJSObjectReference? module;

    private long token;

    [Parameter, EditorRequired]
    public EventCallback OnEscape { get; set; }

    // A selector for content that owns the key while the focus is inside it — the terminal, where
    // Escape is something the agent is meant to receive.
    [Parameter]
    public string? Ignore { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        module = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-escape.js");
        owner = DotNetObjectReference.Create(this);

        token = await module.InvokeAsync<long>("watch", owner, Ignore);
    }

    [JSInvokable]
    public Task Escaped() => OnEscape.InvokeAsync();

    public async ValueTask DisposeAsync()
    {
        if (module is { } loaded)
        {
            try
            {
                await loaded.InvokeVoidAsync("dispose", token);
                await loaded.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        owner?.Dispose();
    }
}
