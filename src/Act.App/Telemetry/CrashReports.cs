using Act.Core.Abstractions;
using Act.Core.Telemetry;

namespace Act.App.Telemetry;

// One place a crash becomes an event, so the cap and the last-chance flush are counted once however
// the exception arrived.
public sealed class CrashReports(ITelemetrySink telemetry)
{
    // A crash loop must not become a send loop: an app failing every few seconds would otherwise
    // spend the rest of its run reporting itself, and the meter counts events.
    private const int Most = 10;

    private static readonly TimeSpan LastFlush = TimeSpan.FromSeconds(2);

    private int reported;

    public void Report(Exception error, bool fatal)
    {
        if (Interlocked.Increment(ref reported) > Most)
            return;

        telemetry.Capture(TelemetryEvents.AppError(error, fatal));

        if (!fatal)
            return;

        // Nothing after a fatal runs, so the queue goes now or not at all. Best effort by definition:
        // the sink swallows and logs its own failures, and a crash report is not worth hanging a
        // dying process over.
        try
        {
            telemetry.FlushAsync().Wait(LastFlush);
        }
        catch
        {
            // Already logged by the sink; there is nowhere useful left to report a failure to report.
        }
    }
}
