namespace Act.App.Desktop;

// The narrowest surface the update pump needs, so the policy — when to check, whether to download,
// what to say about it — lives in `Updates/` where it can be tested, and only the calls that
// genuinely need Electron sit behind this.
//
// **Nothing here throws.** A check that fails is an ordinary outcome, not an exception: the feed is
// on the public internet, the machine may be offline, the release may not exist yet. Every method
// answers with a value that says so, and the implementations are what swallow the difference
// between a 404, a timeout and a socket that never replied.
public interface IUpdater
{
    // False in browser mode, where there is no installer to replace. Everything else is then a
    // no-op and the pump never starts, rather than each caller remembering to ask.
    bool IsSupported { get; }

    // A downloaded update waiting to be applied. Nothing but *Restart and install* applies it, so
    // this only ever gates that: an exit is an exit whatever is on disk.
    //
    // **It goes back to false when a newer version supersedes the one on disk.** Not a latch, because
    // the file it stands for really does leave: electron-updater empties its pending directory as soon
    // as it decides to fetch a different one, so between that moment and the new file landing there is
    // nothing to install and this has to say so.
    bool IsReady { get; }

    // Applied once at startup: pre-releases off, downgrades off, and downloads driven by the policy
    // rather than by the updater deciding for itself. Not async — every one of those is a
    // fire-and-forget message to the Electron main process, and reading any of them back would
    // block a thread on a round trip to learn what we just said.
    void Configure();

    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken);

    // True once the installer for `version` is on disk. False covers every failure, including the
    // download that never finishes — see `ElectronUpdater` for why that one is not hypothetical.
    // `progress` is reported as whole percent, and may not reach 100 even on success.
    //
    // **`version` is what makes this idempotent rather than a latch.** Asked for the version already
    // held it answers immediately; asked for a newer one it goes and gets that instead, and stops
    // claiming to be ready in the meantime.
    Task<bool> DownloadAsync(string version, IProgress<int> progress, CancellationToken cancellationToken);

    // The only thing that applies a downloaded update: ACT closes, the installer runs, and ACT comes
    // back on the new version. Reached from *Restart and install* and from nowhere else — leaving the
    // app does not install, and the installer waits in the updater's cache for however many runs it
    // takes. Does not return.
    //
    // **The app reappearing is the only completion signal there is.** An install started on the way
    // out cannot report anything, because the app reporting it is the app being replaced — which is
    // why a silent install-on-exit reads as ACT going quiet for an indefinite while, and why
    // launching it by hand too early finds shortcuts the installer has not finished rewriting.
    void InstallAndRestart();
}

// **"No new version" and "could not tell" are different answers**, and collapsing them into a null
// version is what would make a private repository read as *You are up to date* forever. `Completed`
// separates them: false means the question went unanswered — offline, 404, a timeout, a build that
// is not packaged — and the only honest thing to show for it is that the check did not happen.
public readonly record struct UpdateCheck(bool Completed, string? Version)
{
    public static UpdateCheck Failed => new(false, null);

    public static UpdateCheck UpToDate => new(true, null);

    public static UpdateCheck Found(string? version) => new(true, version);
}
