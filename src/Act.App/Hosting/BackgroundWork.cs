namespace Act.App.Hosting;

// The lifetime every ACT pump has, once. Five of them wrote it out by hand — a cancellation source,
// some fire-and-forget loops started under it, a `catch (OperationCanceledException)`, and a
// `DisposeAsync` — and the copies had drifted into disagreeing about the two things that matter:
//
//   * **Shutdown waits.** A source disposed while a loop is still awaiting its token throws
//     `ObjectDisposedException` on the way out of the app, and a semaphore disposed under a waiter
//     does the same. So this cancels, waits for everything it started, and only then disposes.
//   * **A failed pass is not a dead loop.** A `catch` around the *loop* means one bad pass ends the
//     polling for the life of the process; a `catch` around the *pass* means it is reported and the
//     next tick tries again. Only the second is right for work that repeats, and it is what
//     `StartLoop` does.
//
// It also owns the single-flight gate, because the queue runner and the retention sweep both need
// one and both need it torn down in the same ordered shutdown as the token — splitting the two
// owners is how the disposal race got in.
public sealed class BackgroundWork(ILogger log) : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly Lock tracking = new();

    private readonly List<Task> running = [];

    private int pending;

    private bool closed;

    public CancellationToken Stopping => stopping.Token;

    // Fire-and-forget to the caller, tracked here, so shutdown can wait for it. Started before the
    // lock is taken rather than under it: a pass runs synchronously up to its first await, and that
    // pass is free to write to the board — which raises the events that call back in here.
    public void Start(string what, Func<CancellationToken, Task> work)
    {
        if (closed || stopping.IsCancellationRequested)
            return;

        var task = Supervised(what, work);

        lock (tracking)
        {
            if (closed)
                return;

            // Pruned on the way in, because a pump starts one of these per live session and would
            // otherwise hold every session it has ever had.
            running.RemoveAll(existing => existing.IsCompleted);
            running.Add(task);
        }
    }

    // A pass immediately, then one per interval until shutdown. Each pass is guarded on its own, so
    // the loop outlives a failure.
    public void StartLoop(string what, TimeSpan interval, Func<CancellationToken, Task> pass)
        => Start(what, async token =>
        {
            using var timer = new PeriodicTimer(interval);

            do
            {
                await Guarded(what, () => pass(token));
            }
            while (await timer.WaitForNextTickAsync(token));
        });

    // One pass at a time, and the requests that arrive while one is running collapse into a single
    // follow-up: a pass that launches five cards raises five board changes, and without the collapse
    // each burst would queue a pass per event that finds nothing left to do.
    public async Task RunAsync(Func<CancellationToken, Task> pass)
    {
        if (Interlocked.Exchange(ref pending, 1) == 1)
            return;

        await gate.WaitAsync(stopping.Token);

        // Cleared inside the gate, so a change that lands *during* the pass still schedules the next
        // one — the flag suppresses duplicates, never the news.
        Interlocked.Exchange(ref pending, 0);

        try
        {
            await pass(stopping.Token);
        }
        finally
        {
            gate.Release();
        }
    }

    // `RunAsync` for an event handler, which has nothing to await it with and no business throwing.
    public void Request(string what, Func<CancellationToken, Task> pass)
        => Start(what, _ => Guarded(what, () => RunAsync(pass)));

    public async ValueTask DisposeAsync()
    {
        Task[] outstanding;

        lock (tracking)
        {
            if (closed)
                return;

            closed = true;
            outstanding = [.. running];

            running.Clear();
        }

        await stopping.CancelAsync();

        // Nothing here can fault: `Supervised` is what every tracked task is wrapped in.
        await Task.WhenAll(outstanding);

        gate.Dispose();
        stopping.Dispose();
    }

    private async Task Supervised(string what, Func<CancellationToken, Task> work)
    {
        try
        {
            await work(stopping.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "{Work} stopped.", what);
        }
    }

    private async Task Guarded(string what, Func<Task> pass)
    {
        try
        {
            await pass();
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "{Work} failed.", what);
        }
    }
}
