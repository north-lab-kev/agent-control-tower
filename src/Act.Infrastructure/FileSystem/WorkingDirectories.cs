using Act.Core.Abstractions;

namespace Act.Infrastructure.FileSystem;

public sealed class WorkingDirectories : IWorkingDirectories
{
    public string Home => WorkingDirectory.Home();

    public string Resolve(string workingDir) => WorkingDirectory.Resolve(workingDir);

    public bool Exists(string workingDir)
        => !string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(Resolve(workingDir));

    public void Create(string workingDir) => Directory.CreateDirectory(Resolve(workingDir));

    public PathCheck Check(string workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return PathCheck.Malformed(PathError.Empty);

        var typed = workingDir.Trim();

        // A relative path would resolve against whatever directory ACT happens to be running in,
        // which is not a place the user was thinking of. Refuse rather than silently pick one.
        if (!WorkingDirectory.IsHomeRelative(typed) && !Path.IsPathRooted(typed))
            return PathCheck.Malformed(PathError.NotAbsolute);

        string resolved;

        try
        {
            resolved = Resolve(typed);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException or System.Security.SecurityException)
        {
            // The invalid-character set differs per OS, so the portable test is whether the
            // framework itself can make sense of the path rather than a hand-written char list.
            return PathCheck.Malformed(PathError.Malformed);
        }

        return Directory.Exists(resolved) ? PathCheck.Found(resolved) : PathCheck.Missing(resolved);
    }

    public string NearestDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Home;

        var check = Check(path);

        if (!check.WellFormed)
            return Home;

        var candidate = check.Resolved;

        // A file is a perfectly good starting point — it just means the folder holding it.
        if (File.Exists(candidate))
            candidate = Parent(candidate) ?? string.Empty;

        while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
            candidate = Parent(candidate) ?? string.Empty;

        return string.IsNullOrEmpty(candidate) ? Home : candidate;
    }

    public DirectoryListing List(string? path, bool includeFiles = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DirectoryListing(null, null, Roots());

        string resolved;

        try
        {
            resolved = Resolve(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException or System.Security.SecurityException)
        {
            return new DirectoryListing(path, null, [], PathError.Malformed);
        }

        if (!Directory.Exists(resolved))
            return new DirectoryListing(resolved, Parent(resolved), [], PathError.Missing);

        try
        {
            var directories = Directory.EnumerateDirectories(resolved)
                .Select(child => new DirectoryEntry(Path.GetFileName(child), child))
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var files = includeFiles
                ? Directory.EnumerateFiles(resolved)
                    .Select(child => new DirectoryEntry(Path.GetFileName(child), child))
                    .Where(entry => !string.IsNullOrEmpty(entry.Name))
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            return new DirectoryListing(resolved, Parent(resolved), directories, Files: files);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            // A folder you cannot read is a normal thing to meet while browsing — `C:\System
            // Volume Information`, another user's home — so it reports rather than throws.
            return new DirectoryListing(resolved, Parent(resolved), [], PathError.Unreadable);
        }
    }

    // Null at a root, so the picker knows there is no "up" from `C:\` or `/`.
    //
    // The root test has to come first. Trimming the separator off `C:\` leaves `C:`, which Windows
    // reads as "the current directory on drive C" rather than the drive itself — `GetParent` then
    // answers with wherever the process happens to be running. The trim is still needed below,
    // because `GetParent("C:\dev\")` treats the trailing separator as an empty last segment and
    // returns `C:\dev` — itself.
    private static string? Parent(string path)
    {
        var full = Path.GetFullPath(path);

        return string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)
            ? null
            : Directory.GetParent(full.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
    }

    private static IReadOnlyList<DirectoryEntry> Roots()
    {
        if (!OperatingSystem.IsWindows())
            return [new DirectoryEntry("/", "/")];

        return
        [
            .. DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new DirectoryEntry(
                    drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                    drive.RootDirectory.FullName)),
        ];
    }
}
