using Act.App.Desktop;

namespace Act.App.UiTests;

// A scriptable stand-in for `ElectronUpdater`. Counts as well as answers, because most of what the
// pump owes is about restraint — `Off` must not check, `NotifyOnly` must not download — and a count
// is the only way to assert that something did *not* happen.
internal sealed class FakeUpdater : IUpdater
{
    public bool IsSupported { get; set; } = true;

    public bool IsReady { get; private set; }

    public int Configured { get; private set; }

    public int Checks { get; private set; }

    public int Downloads { get; private set; }

    public int Installs { get; private set; }

    // What the next check answers. Defaults to the quiet outcome, so a test that never sets it is
    // testing a machine that cannot reach the feed — which is the state ACT ships in today.
    public UpdateCheck Answer { get; set; } = UpdateCheck.Failed;

    public bool DownloadSucceeds { get; set; } = true;

    // Reported before the download resolves, so a test can pin the progress the page renders.
    public int[] ProgressSteps { get; set; } = [];

    public void Configure() => Configured++;

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        Checks++;

        return Task.FromResult(Answer);
    }

    public Task<bool> DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        Downloads++;

        foreach (var percent in ProgressSteps)
            progress.Report(percent);

        if (DownloadSucceeds)
            IsReady = true;

        return Task.FromResult(DownloadSucceeds);
    }

    public void InstallAndExit() => Installs++;
}
