namespace Act.Core.Abstractions;

// One rung of the folder picker. `Parent` is null at a root, which is how the picker knows not to
// offer "up"; `Roots` are the drives on Windows and `/` elsewhere, so the same walk works on both.
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Directories,
    string? Error = null);

public sealed record DirectoryEntry(string Name, string Path);
