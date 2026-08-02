using System.Text;
using Act.Core.Abstractions;
using Porta.Pty;

namespace Act.Infrastructure.Terminal;

// One live pseudo-terminal. Two details here are load-bearing and should not be tidied away:
// output is decoded through a stateful `Decoder` because a UTF-8 sequence can straddle two
// reads, and it is batched on a timer because a full-screen TUI redraws far more often than
// a Blazor circuit wants to be poked. Both were established by the launch prototype.
internal sealed class PtyProcess(IPtyConnection connection) : IPtyProcess
{
    private const int FlushMilliseconds = 30;

    private const int BufferCeiling = 1 << 20;

    private readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();

    private readonly StringBuilder pending = new();

    private readonly Lock gate = new();

    private readonly CancellationTokenSource stopping = new();

    private Task? reader;

    private Task? flusher;

    private bool disposed;

    public event Func<string, Task>? Output;

    public event Action<int>? Exited;

    public int ProcessId => connection.Pid;

    internal void Start()
    {
        connection.ProcessExited += OnProcessExited;

        reader = Task.Run(() => ReadAsync(stopping.Token));
        flusher = Task.Run(() => FlushAsync(stopping.Token));
    }

    public async Task WriteAsync(string data, CancellationToken cancellationToken = default)
    {
        if (disposed || data.Length == 0)
            return;

        var bytes = Encoding.UTF8.GetBytes(data);

        await connection.WriterStream.WriteAsync(bytes, cancellationToken);
        await connection.WriterStream.FlushAsync(cancellationToken);
    }

    public void Resize(int cols, int rows)
    {
        if (disposed || cols <= 0 || rows <= 0)
            return;

        try
        {
            connection.Resize(cols, rows);
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        connection.ProcessExited -= OnProcessExited;

        await stopping.CancelAsync();

        try
        {
            connection.Kill();
        }
        catch (InvalidOperationException)
        {
        }

        foreach (var task in new[] { reader, flusher })
        {
            if (task is null)
                continue;

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Killing the agent is not enough. The pseudo-console is a separate object owning its own
        // `conhost.exe`, and it outlives the process that was attached to it — so skipping this
        // leaks one conhost per session ACT ever launches.
        connection.Dispose();

        stopping.Dispose();
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var characters = new char[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await connection.ReaderStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                var decoded = decoder.GetChars(buffer, 0, read, characters, 0);
                if (decoded == 0)
                    continue;

                lock (gate)
                {
                    if (pending.Length < BufferCeiling)
                        pending.Append(characters, 0, decoded);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(FlushMilliseconds));

        try
        {
            while (await ticker.WaitForNextTickAsync(cancellationToken))
            {
                if (Output is not { } handler)
                    continue;

                string batch;

                lock (gate)
                {
                    if (pending.Length == 0)
                        continue;

                    batch = pending.ToString();
                    pending.Clear();
                }

                await handler(batch);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs args) => Exited?.Invoke(args.ExitCode);
}
