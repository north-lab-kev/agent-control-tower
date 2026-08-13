namespace Act.App.Desktop;

public interface IDesktopBridge
{
    bool IsDesktop { get; }

    Task OpenExternalAsync(string url);

    // Hands a path to the OS to open with whatever it is associated with — a folder in its file
    // manager, a file in its default application. One method for both because that is one operation to
    // the shell (`shell.openPath`), and a separate `OpenFolderAsync` only invited a call site to read
    // as a bug the first time it was handed a file.
    //
    // Returns **null on success and a message on failure**, because failure here is ordinary and
    // silent: a file extension with no associated program is a shrug from the OS, not an exception, and
    // a click that appears to do nothing is the worst version of that.
    Task<string?> OpenPathAsync(string path);

    // *Restart and install*, offered by the Settings page and the title bar. On the bridge rather
    // than on `IUpdater` because a restart is an exit that comes back: the live sessions have to end
    // first and running work has to be asked about, exactly as the tray's *Exit* does, and none of
    // that is the updater's business. A no-op where there is no installer to run.
    Task RestartForUpdateAsync();
}
