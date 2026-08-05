using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Telemetry;

namespace Act.App.Telemetry;

// The front gate: the switch is read on every capture rather than once at startup, so turning it off
// takes effect on the next event instead of the next run — and so does turning it back on, which a
// startup read would have made impossible without a restart.
//
// It is deliberately *not* the only gate. Nothing that gets past here has been sent yet: the
// transport batches, and its own timer would deliver a batch queued before the switch moved. The
// second gate in `ActTelemetry` is what stops that one. Together they mean off is immediate for the
// queue as well as for the call sites.
public sealed class ConsentedTelemetrySink(ITelemetrySink transport, UserSettingsService settings) : ITelemetrySink
{
    public void Capture(TelemetryEvent telemetry)
    {
        if (!settings.Telemetry)
            return;

        transport.Capture(telemetry);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
        => settings.Telemetry ? transport.FlushAsync(cancellationToken) : Task.CompletedTask;
}
