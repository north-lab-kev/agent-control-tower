using Act.Core.Abstractions;

namespace Act.Infrastructure.Terminal;

// The OS half of install discovery. Every method answers "what is there" and none of them throws:
// a probe runs at startup against paths that may not exist, on a machine where an agent may not be
// installed at all, and none of that is a failure worth stopping a launch over.
internal sealed class ExecutableProbe : IExecutableProbe
{
    public string? OnPath(string executable)
    {
        var resolved = ExecutableResolver.Resolve(executable);

        // The resolver hands the original name back when it finds nothing, so an unchanged answer
        // is the miss.
        return resolved == executable ? null : resolved;
    }

    public string? FirstExisting(IEnumerable<string> candidates)
        => candidates.FirstOrDefault(Exists);

    public IReadOnlyList<string> DirectoriesNewestFirst(string parent)
    {
        try
        {
            if (!Directory.Exists(parent))
                return [];

            return [.. new DirectoryInfo(parent)
                .GetDirectories()
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .Select(directory => directory.FullName)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Exists(string candidate)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
