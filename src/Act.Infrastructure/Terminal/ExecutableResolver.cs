namespace Act.Infrastructure.Terminal;

// A pseudo-terminal spawns through CreateProcess/exec with an explicit image path, so unlike a
// shell it does not walk `PATH` — handed a bare `claude` it looks for a file of that name in
// the working directory and fails. Adapters name their binary the way a user would type it, so
// the lookup belongs here, on the one side of the seam that is allowed to know about the OS.
internal static class ExecutableResolver
{
    public static string Resolve(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar)
            || executable.Contains(Path.AltDirectorySeparatorChar))
            return executable;

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in directories)
        {
            foreach (var extension in Extensions())
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory.Trim(), executable + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is the user's, not ours; skip it rather than fail
                    // the launch over someone else's stray quote.
                    continue;
                }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Hand the original back and let the spawn produce the OS's own "not found" error,
        // which names the binary the user configured rather than something ACT invented.
        return executable;
    }

    private static IEnumerable<string> Extensions()
    {
        if (!OperatingSystem.IsWindows())
            return [string.Empty];

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? [".EXE", ".CMD", ".BAT", ".COM"]
            : pathExt.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return [string.Empty, .. extensions];
    }
}
