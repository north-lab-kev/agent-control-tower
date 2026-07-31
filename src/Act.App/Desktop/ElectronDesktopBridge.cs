using ElectronNET.API;

namespace Act.App.Desktop;

public sealed class ElectronDesktopBridge : IDesktopBridge
{
    public bool IsDesktop => true;

    public Task OpenExternalAsync(string url) => Electron.Shell.OpenExternalAsync(url);
}
