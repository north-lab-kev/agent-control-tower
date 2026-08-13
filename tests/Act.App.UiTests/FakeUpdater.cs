using Act.App.Desktop;

namespace Act.App.UiTests;

// A scriptable stand-in for `ElectronUpdater`. Counts as well as answers, because most of what the
// pump owes is about restraint — the toggle off must not check on its own, and a manual check must not
// download — and a count is the only way to assert that something did *not* happen.
internal sealed class FakeUpdater : IUpdater
{
    public bool IsSupported { get; set; } = true;

    public bool IsReady { get; private set; }

    // The version behind `IsReady`, so a test can assert *which* installer is on disk after a newer one
    // supersedes an older one — the difference the version argument exists to make.
    public string? ReadyVersion { get; private set; }

    // Every version `DownloadAsync` was asked for, in order. A count cannot tell a re-download of 1.3.0
    // from a second look at 1.2.0, and that distinction is the whole of the supersede behaviour.
    public List<string> Requested { get; } = [];

    public int Configured { get; private set; }

    public int Checks { get; private set; }

    public int Downloads { get; private set; }

    public int Restarts { get; private set; }

    // What the next check answers. Defaults to the quiet outcome, so a test that never sets it is
    // testing a machine that cannot reach the feed — which is the state ACT ships in today.
    public UpdateCheck Answer { get; set; } = UpdateCheck.Failed;

    public bool DownloadSucceeds { get; set; } = true;

    // What a bridge that has moved under `ElectronUpdater` looks like from here: `Configure` is the
    // one call that touches Electron before anything is running, and it threw for real.
    public Exception? ConfigureThrows { get; set; }

    // Reported before the download resolves, so a test can pin the progress the page renders.
    public int[] ProgressSteps { get; set; } = [];

    // Kept so a test can fire a report *after* the download resolved, which is not a contrivance:
    // `Progress<T>` posts its callback rather than running it inline, so that is the ordinary
    // arrival order and the one that used to bury the finished download.
    public IProgress<int>? Reporter { get; private set; }

    public void Configure()
    {
        Configured++;

        if (ConfigureThrows is { } error)
            throw error;
    }

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        Checks++;

        return Task.FromResult(Answer);
    }

    // Modelled on `ElectronUpdater` down to the destructive part: asked for a version other than the one
    // held, it stops being ready *before* it tries, because electron-updater has emptied its cache by
    // then. A test that assumed otherwise would be testing a kinder updater than the real one.
    public Task<bool> DownloadAsync(string version, IProgress<int> progress, CancellationToken cancellationToken)
    {
        Requested.Add(version);

        if (IsReady && ReadyVersion == version)
            return Task.FromResult(true);

        IsReady = false;
        ReadyVersion = null;

        Downloads++;

        Reporter = progress;

        foreach (var percent in ProgressSteps)
            progress.Report(percent);

        if (DownloadSucceeds)
        {
            IsReady = true;
            ReadyVersion = version;
        }

        return Task.FromResult(DownloadSucceeds);
    }

    // Counted rather than answered: the only install ACT has is the one the user asked for, so what a
    // test needs from it is whether it happened at all.
    public void InstallAndRestart() => Restarts++;
}
