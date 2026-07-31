using Microsoft.JSInterop;

namespace Act.App.Desktop;

public sealed class BrowserDesktopBridge(IJSRuntime js) : IDesktopBridge
{
    public bool IsDesktop => false;

    public async Task OpenExternalAsync(string url) => await js.InvokeVoidAsync("open", url, "_blank");
}
