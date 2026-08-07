using System.Diagnostics;
using System.Text;
using Act.Core.Abstractions;

namespace Act.Infrastructure.Terminal;

// `ICommandHost` over a plain redirected process. Sits beside `PtyHost` because the two answer the
// same question — how does ACT start a CLI on this machine — and share the `PATH` walk; everything
// else about them is opposite.
//
// Three things here are not obvious and all three were measured against the pinned CLIs:
//
//   * **Standard input is always redirected, and always closed.** Codex inspects its stdin and
//     announces "Reading additional input from stdin…"; inherit ACT's own and it waits on a handle
//     that will never close. Closing it immediately is what makes the run terminate, and it is also
//     the channel the prompt itself travels on.
//   * **The two streams are read concurrently, before the wait.** A CLI that fills the stderr pipe
//     while ACT is blocked reading stdout deadlocks, and Codex writes far more to stderr than to
//     stdout.
//   * **A `.cmd` or `.bat` cannot be started directly** with `UseShellExecute = false` — Windows
//     answers 193, "not a valid Win32 application" — and a global npm install of either CLI is
//     exactly that shim. It goes through `cmd /c`, which is safe here only because the one argument
//     carrying arbitrary text travels on stdin instead of the command line.
public sealed class CommandHost : ICommandHost
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    public async Task<CommandResult> RunAsync(
        CommandStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = Configure(startInfo) };

        process.Start();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(startInfo.Timeout ?? DefaultTimeout);

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        await WriteInputAsync(process, startInfo.Input);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);

            // The caller's own cancellation is a caller's problem and propagates; the deadline is
            // ours and comes back as a result, because "it took too long" is an answer.
            cancellationToken.ThrowIfCancellationRequested();

            return new CommandResult(-1, string.Empty, string.Empty, TimedOut: true);
        }

        return new CommandResult(process.ExitCode, (await output).Trim(), (await error).Trim());
    }

    private static ProcessStartInfo Configure(CommandStartInfo startInfo)
    {
        var executable = ExecutableResolver.Resolve(startInfo.Executable);

        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = startInfo.WorkingDir,
        };

        if (NeedsShim(executable))
        {
            info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(executable);
        }
        else
        {
            info.FileName = executable;
        }

        foreach (var argument in startInfo.Arguments)
            info.ArgumentList.Add(argument);

        // Assigned rather than merged: the caller already inherited what it wanted to keep, and a
        // half-inherited environment is the harder thing to reason about.
        info.Environment.Clear();

        foreach (var entry in startInfo.Environment)
            info.Environment[entry.Key] = entry.Value;

        return info;
    }

    private static bool NeedsShim(string executable)
        => OperatingSystem.IsWindows()
            && Path.GetExtension(executable) is ".cmd" or ".bat";

    // Closed either way, and that is the load-bearing half — see the class note. A pipe that has
    // already gone (a CLI that exited before it read anything) is not a failure of the run.
    private static async Task WriteInputAsync(Process process, string? input)
    {
        try
        {
            if (input is { Length: > 0 })
                await process.StandardInput.WriteAsync(input);
        }
        catch (IOException)
        {
        }
        finally
        {
            process.StandardInput.Close();
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
        }
    }
}
