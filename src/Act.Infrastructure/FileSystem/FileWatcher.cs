using System.Diagnostics;

namespace Act.Infrastructure.FileSystem;

public interface IFileWatcher
{
    IFileWatch Watch(string path);
}

public interface IFileWatch : IDisposable
{
    void Reset();

    Task<bool> WaitForChangeAsync(TimeSpan wait, TimeSpan floor, CancellationToken cancellationToken = default);
}

public sealed class FileWatcher : IFileWatcher
{
    public IFileWatch Watch(string path) => new FileWatch(path);

    private sealed class FileWatch : IFileWatch
    {
        private readonly Lock gate = new();

        private readonly FileSystemWatcher? watcher;

        private TaskCompletionSource<bool> changed = NewSignal();

        internal FileWatch(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(file) || !Directory.Exists(directory))
                return;

            try
            {
                watcher = new FileSystemWatcher(directory, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.FileName
                        | NotifyFilters.Size
                        | NotifyFilters.CreationTime,
                };

                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception error) when (error is IOException or ArgumentException)
            {
                watcher?.Dispose();
                watcher = null;
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                if (changed.Task.IsCompleted)
                    changed = NewSignal();
            }
        }

        public async Task<bool> WaitForChangeAsync(
            TimeSpan wait,
            TimeSpan floor,
            CancellationToken cancellationToken = default)
        {
            if (watcher is null)
            {
                await Task.Delay(wait, cancellationToken);

                return false;
            }

            var started = Stopwatch.GetTimestamp();

            Task<bool> signal;

            lock (gate)
                signal = changed.Task;

            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var timeout = Task.Delay(wait, expiry.Token);
            var first = await Task.WhenAny(signal, timeout);

            if (first == timeout)
            {
                await timeout;

                return false;
            }

            await expiry.CancelAsync();

            var remaining = floor - Stopwatch.GetElapsedTime(started);

            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, cancellationToken);

            return true;
        }

        public void Dispose() => watcher?.Dispose();

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            lock (gate)
                changed.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
