using System.Globalization;
using System.Runtime.InteropServices;
using Act.App.Hosting;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Telemetry;

namespace Act.App.Telemetry;

// The one event nobody else is placed to send: the run itself, carrying the configuration it ran
// under. Started early in `Program` and stopped by the container on the way out, which is where the
// last flush lives.
//
// **Crashes are not here.** `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`
// are only two of the four places an unhandled exception lands — a Blazor event handler reaches
// neither — so `TelemetryErrorBridge` listens where all four converge, and `StartupLog` already logs
// those two with the exception attached; reporting them here as well would report them twice.
//
// Shutdown sends nothing of its own. It only flushes: a queued batch that never left would take the
// run's events with it.
public sealed class TelemetryPump(ITelemetrySink telemetry, UserSettingsService settings) : IAsyncDisposable
{
    private bool flushed;

    public void Start()
        => telemetry.Capture(settings.TelemetryStarted(AppVersion.Current, RuntimeInformation.OSDescription, Locale()));

    public async ValueTask DisposeAsync()
    {
        if (flushed)
            return;

        flushed = true;

        await telemetry.FlushAsync();
    }

    private static string Locale() => CultureInfo.CurrentUICulture.Name;
}
