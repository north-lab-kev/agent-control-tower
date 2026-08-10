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
// **Crashes are not here.** They used to be, as subscriptions to `AppDomain.UnhandledException` and
// `TaskScheduler.UnobservedTaskException` — which are only two of the four places an unhandled
// exception lands, and not the two that matter most: a Blazor event handler never reaches either, so
// most of ACT's exceptions were invisible. `TelemetryErrorBridge` listens where all four converge
// instead, and `StartupLog` already logs these two with the exception attached, so keeping them here
// would only have reported them twice.
//
// Shutdown sends nothing of its own. It only flushes: a queued batch that never left would take the
// run's events with it. (An `app_stopped` uptime was here until 2026-08-05 and was removed as an
// event nobody would act on.)
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
