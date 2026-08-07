namespace Act.Core.Abstractions;

// One rung of the path picker. `Parent` is null at a root, which is how the picker knows not to
// offer "up"; `Roots` are the drives on Windows and `/` elsewhere, so the same walk works on both.
//
// `Files` is empty unless the caller asked for it. Directories are cheap to enumerate and always
// wanted; files are neither — picking a working directory has no use for the 40,000 files in a
// `node_modules`, and paying to list them on every rung of the walk would be felt.
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Directories,
    string? Error = null,
    IReadOnlyList<DirectoryEntry>? Files = null)
{
    public IReadOnlyList<DirectoryEntry> Files { get; init; } = Files ?? [];
}

public sealed record DirectoryEntry(string Name, string Path);
