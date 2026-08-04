namespace Act.App.Desktop;

public interface IDesktopBridge
{
    bool IsDesktop { get; }

    Task OpenExternalAsync(string url);

    Task OpenFolderAsync(string path);
}
