using Act.Core.Abstractions;

namespace Act.App.UiTests;

// Every path answer, in memory. Permissive by default — resolution normalises slashes and everything
// exists — because that is the shape in which ACT changes nothing, and the tests that care about a
// refusal say so by setting `Check` or `Fails`.
internal sealed class FakeWorkingDirectories : IWorkingDirectories
{
    private readonly Dictionary<string, DirectoryListing> listings = [];

    public string Home { get; set; } = "/home/act";

    public List<string> Created { get; } = [];

    // What `Check` should answer, keyed by the resolved path. Anything not named is found.
    public Dictionary<string, PathCheck> Checks { get; } = [];

    public Exception? CreateFails { get; set; }

    public string Resolve(string workingDir) => workingDir.Trim().Replace('\\', '/');

    public bool Exists(string workingDir) => Check(workingDir).Exists;

    public void Create(string workingDir)
    {
        if (CreateFails is { } failure)
            throw failure;

        Created.Add(Resolve(workingDir));
    }

    public PathCheck Check(string workingDir)
        => Checks.TryGetValue(Resolve(workingDir), out var answer) ? answer : PathCheck.Found(Resolve(workingDir));

    // Walks up to the nearest path this fake was told about, the way the real port walks to the nearest
    // path that is really a directory — a file name or a folder that does not exist yet has to land on
    // its parent, which is exactly what the picker opens at.
    public string NearestDirectory(string? path)
    {
        if (path is null or "")
            return Home;

        for (var candidate = Resolve(path); candidate.Length > 0; candidate = Parent(candidate) ?? string.Empty)
        {
            if (listings.ContainsKey(candidate))
                return candidate;
        }

        return Home;
    }

    public DirectoryListing List(string? path, bool includeFiles = false)
        => listings.TryGetValue(path ?? string.Empty, out var listing) ? listing : new DirectoryListing(path, null, []);

    // The listing a walk finds at `path`. Named entries only, so a test describes a tree by its
    // shape rather than by a set of absolute paths.
    public FakeWorkingDirectories With(string? path, params string[] directories)
    {
        listings[path ?? string.Empty] = new DirectoryListing(path, Parent(path), Entries(path, directories));

        return this;
    }

    public FakeWorkingDirectories WithFiles(string? path, string[] directories, params string[] files)
    {
        listings[path ?? string.Empty] = new DirectoryListing(
            path,
            Parent(path),
            Entries(path, directories),
            Files: Entries(path, files));

        return this;
    }

    private static DirectoryEntry[] Entries(string? path, string[] names)
        => [.. names.Select(name => new DirectoryEntry(name, $"{path?.TrimEnd('/')}/{name}"))];

    public FakeWorkingDirectories WithError(string? path, string error)
    {
        listings[path ?? string.Empty] = new DirectoryListing(path, Parent(path), [], error);

        return this;
    }

    private static string? Parent(string? path)
    {
        if (path is null)
            return null;

        var cut = path.TrimEnd('/').LastIndexOf('/');

        return cut <= 0 ? null : path[..cut];
    }
}
