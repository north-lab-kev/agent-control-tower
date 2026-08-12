using Act.Core.Telemetry;

namespace Act.Core.Abstractions;

public interface ITelemetrySink
{
    void Capture(TelemetryEvent telemetry);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
