using ElectronNET.API;

namespace Act.App.Desktop;

public sealed class ElectronDesktopBridge : IDesktopBridge
{
    public bool IsDesktop => true;

    public Task OpenExternalAsync(string url) => Electron.Shell.OpenExternalAsync(url);

    // `openPath` answers with an empty string on success and a message on failure rather than throwing,
    // so the empty case is normalised to null and the message passed straight through.
    public async Task<string?> OpenPathAsync(string path)
    {
        var error = await Electron.Shell.OpenPathAsync(path);

        return string.IsNullOrWhiteSpace(error) ? null : error;
    }
}
