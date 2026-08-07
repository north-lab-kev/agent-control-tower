using Act.Core.Abstractions;
using Act.Core.Telemetry;

namespace Act.Infrastructure.Telemetry;

public sealed class NullTelemetrySink : ITelemetrySink
{
    public void Capture(TelemetryEvent telemetry)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
