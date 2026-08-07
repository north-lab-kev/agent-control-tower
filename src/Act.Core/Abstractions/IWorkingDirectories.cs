namespace Act.Core.Abstractions;

// A card stores its working directory the way the user typed it — `~/dev/act`, `c:/dev/test/` —
// and something has to turn that into a real path, judge whether it is usable, and let the user
// go and find one. A port rather than a helper because the same answers are needed by the launch,
// by the form, by the folder picker, and by the `.act/` file watchers at step 8.
public interface IWorkingDirectories
{
    string Resolve(string workingDir);

    bool Exists(string workingDir);

    void Create(string workingDir);

    PathCheck Check(string workingDir);

    // `path` null lists the roots — drives on Windows, `/` elsewhere — so the picker has a place
    // to start on either OS without asking which one it is on. `includeFiles` is off by default:
    // the directory walk is the common case and listing files costs real time in a big tree.
    DirectoryListing List(string? path, bool includeFiles = false);

    // Where a walk should open for a path that may be a file, may not exist yet, or may be
    // nonsense: the nearest ancestor that is a real directory, and `Home` when there is none.
    // Here rather than in the picker because every step of it — resolution, the Windows root
    // rule, what counts as a directory — is this port's knowledge.
    string NearestDirectory(string? path);

    string Home { get; }
}
