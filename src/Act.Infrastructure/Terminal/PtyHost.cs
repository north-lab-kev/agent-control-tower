using Act.Core.Abstractions;
using Porta.Pty;

namespace Act.Infrastructure.Terminal;

public sealed class PtyHost : IPtyHost
{
    public async Task<IPtyProcess> StartAsync(
        PtyStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        var options = new PtyOptions
        {
            Name = "ACT",
            App = ExecutableResolver.Resolve(startInfo.Executable),
            CommandLine = [.. startInfo.Arguments],
            Cwd = startInfo.WorkingDir,
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
