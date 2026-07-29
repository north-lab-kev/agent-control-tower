namespace Act.Infrastructure.FileSystem;

// `~` is a shell convention, not an operating-system one: nothing below a shell expands it, so a
// working directory of `~/dev/act` reaches CreateProcess verbatim and fails as an invalid
// directory name. ACT invites those paths — the new-task form's own placeholder is `~/dev/act` —
// so it has to expand them itself.
internal static class WorkingDirectory
{
    public static bool IsHomeRelative(string path)
        => path is "~"
            || path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith(@"~\", StringComparison.Ordinal);

    public static string Resolve(string workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return Directory.GetCurrentDirectory();

        var path = workingDir.Trim();

        if (path is "~")
            path = Home();
        else if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
            path = Path.Combine(Home(), path[2..]);

        // Forward slashes survive on Windows in most APIs but not all, and a mixed path is
        // miserable to read in an error message.
        path = path.Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(path);
    }

    public static string Home() => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);
}
