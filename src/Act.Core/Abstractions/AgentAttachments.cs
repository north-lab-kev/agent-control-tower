namespace Act.Core.Abstractions;

// The task's files as an adapter needs them: absolute paths already resolved, and the directory
// they share. Paths rather than a store handle, for the same reason `IPtyHost` is a port — the
// adapter contract suite has to stay off the filesystem.
//
// `Directory` travels even when `Files` is empty, and travels on a *resume* too, because the
// directory is what a CLI is granted access to for the whole session: a file dropped onto a live
// terminal an hour in lands there, and nothing gets to re-run the command line by then.
public sealed record AgentAttachments(string Directory, IReadOnlyList<AgentAttachment> Files)
{
    public static AgentAttachments None { get; } = new(string.Empty, []);

    public IReadOnlyList<string> PathsOf(bool image)
        => [.. Files.Where(file => file.IsImage == image).Select(file => file.Path)];

    public IReadOnlyList<string> AllPaths => [.. Files.Select(file => file.Path)];
}

public sealed record AgentAttachment(string Path, bool IsImage);
