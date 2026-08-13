using ElectronNET.API;

namespace Act.App.Desktop;

// Takes the shell rather than the updater: the restart has to end the sessions and ask about running
// work first, and that lives there. Safe to depend on — `DesktopShell` takes no bridge, so this does
// not close the loop the way `INotifier -> DesktopShell -> UpdatePump -> INotifier` would.
public sealed class ElectronDesktopBridge(DesktopShell shell) : IDesktopBridge
{
    public bool IsDesktop => true;

    public Task OpenExternalAsync(string url) => Electron.Shell.OpenExternalAsync(url);

    public Task RestartForUpdateAsync() => shell.RestartForUpdateAsync();

    // `openPath` answers with an empty string on success and a message on failure rather than throwing,
    // so the empty case is normalised to null and the message passed straight through.
    public async Task<string?> OpenPathAsync(string path)
    {
        var error = await Electron.Shell.OpenPathAsync(path);

        return string.IsNullOrWhiteSpace(error) ? null : error;
    }
}
