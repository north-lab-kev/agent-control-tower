namespace Act.App.Desktop;

// Browser mode has no installer to replace, so every answer is the one that makes the pump stand
// down. Silent rather than throwing, like `BrowserNotifier`: a fallback that throws turns "this
// mode does not have that" into a bug report.
public sealed class BrowserUpdater : IUpdater
{
    public bool IsSupported => false;

    public bool IsReady => false;

    public void Configure()
    {
    }

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(UpdateCheck.Failed);

    public Task<bool> DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public void InstallAndRestart()
    {
    }
}
