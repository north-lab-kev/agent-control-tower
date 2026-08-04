using System.Diagnostics;
using Microsoft.JSInterop;

namespace Act.App.Desktop;

public sealed class BrowserDesktopBridge(IJSRuntime js) : IDesktopBridge
{
    public bool IsDesktop => false;

    public async Task OpenExternalAsync(string url) => await js.InvokeVoidAsync("open", url, "_blank");

    public Task OpenFolderAsync(string path)
    {
        using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Task.CompletedTask;
    }
}
