using System.Diagnostics;
using Microsoft.JSInterop;

namespace Act.App.Desktop;

public sealed class BrowserDesktopBridge(IJSRuntime js) : IDesktopBridge
{
    public bool IsDesktop => false;

    public async Task OpenExternalAsync(string url) => await js.InvokeVoidAsync("open", url, "_blank");

    // A browser tab cannot replace its own installer, and the button that would call this is not
    // rendered here — this is the fallback that keeps that a layout decision rather than a crash.
    public Task RestartForUpdateAsync() => Task.CompletedTask;

    // `UseShellExecute` on the **server**, which for ACT is the machine the user is sitting at — the web
    // host is an implementation detail of a desktop app, not a deployment. That is the same trust model
    // the logs folder already opens under; it is worth naming because it is the one line here that
    // would mean something different if ACT were ever served to another machine.
    //
    // Unlike Electron's `openPath` this throws rather than reporting, so the throw becomes the message.
    public Task<string?> OpenPathAsync(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            return Task.FromResult<string?>(null);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception
            or InvalidOperationException or FileNotFoundException or PlatformNotSupportedException)
        {
            return Task.FromResult<string?>(error.Message);
        }
    }
}
