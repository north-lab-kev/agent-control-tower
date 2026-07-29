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
    // to start on either OS without asking which one it is on.
    DirectoryListing List(string? path);

    string Home { get; }
}
