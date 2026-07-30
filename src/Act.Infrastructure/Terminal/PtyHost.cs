using Act.Core.Abstractions;
using Act.Infrastructure.FileSystem;
using Porta.Pty;

namespace Act.Infrastructure.Terminal;

public sealed class PtyHost(IWorkingDirectories directories) : IPtyHost
{
    public async Task<IPtyProcess> StartAsync(
        PtyStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        var workingDir = directories.Resolve(startInfo.WorkingDir);

        // Checked here rather than left to the spawn: the pty reports a missing directory as
        // "The directory name is invalid" appended to the whole command line, which buries the one
        // fact that matters. This says which directory, in the form the user typed it.
        if (!directories.Exists(startInfo.WorkingDir))
            throw new DirectoryNotFoundException(
                $"Working directory not found: {startInfo.WorkingDir}"
                + (workingDir == startInfo.WorkingDir ? string.Empty : $" (resolved to {workingDir})"));

        var options = new PtyOptions
        {
            Name = "ACT",
            App = ExecutableResolver.Resolve(startInfo.Executable),
            CommandLine = [.. startInfo.Arguments],
            Cwd = workingDir,
            Cols = startInfo.Size.Cols,
            Rows = startInfo.Size.Rows,
            Environment = new Dictionary<string, string>(
                startInfo.Environment,
                StringComparer.OrdinalIgnoreCase),
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        var process = new PtyProcess(connection);

        process.Start();

        return process;
    }
}
