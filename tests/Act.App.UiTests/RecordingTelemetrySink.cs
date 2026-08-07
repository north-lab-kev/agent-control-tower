using Act.Core.Abstractions;
using Act.Core.Telemetry;

namespace Act.App.UiTests;

public sealed class RecordingTelemetrySink : ITelemetrySink
{
    public List<TelemetryEvent> Captured { get; } = [];

    public int Flushes { get; private set; }

    public IEnumerable<TelemetryEvent> Named(string name) => Captured.Where(entry => entry.Name == name);

    public void Capture(TelemetryEvent telemetry) => Captured.Add(telemetry);

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Flushes++;

        return Task.CompletedTask;
    }
}
