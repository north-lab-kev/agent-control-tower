using Act.Core.Abstractions;

namespace Act.TestSupport;

// The path answers pure-logic tests need from `IWorkingDirectories`, with no filesystem behind them:
// a path is well-formed when it is spelled absolutely (or `~`-relative), and everything well-formed
// exists. Lexical on purpose — `Path.IsPathRooted` would make `C:/x` a different answer per OS, and
// a resolver test has to pass on both.
public sealed class StubWorkingDirectories : IWorkingDirectories
{
    public string Home => "/home/act";

    public string Resolve(string workingDir) => workingDir.Trim();

    public bool Exists(string workingDir) => Check(workingDir).Exists;

    public void Create(string workingDir)
    {
    }

    public PathCheck Check(string workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return PathCheck.Malformed(PathError.Empty);

        var typed = workingDir.Trim();

        return Rooted(typed)
            ? PathCheck.Found(typed)
            : PathCheck.Malformed(PathError.NotAbsolute);
    }

    public DirectoryListing List(string? path, bool includeFiles = false) => new(path, null, []);

    public string NearestDirectory(string? path) => Home;

    private static bool Rooted(string path)
        => path.StartsWith('/')
            || path.StartsWith('~')
            || path.StartsWith('\\')
            || (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}
