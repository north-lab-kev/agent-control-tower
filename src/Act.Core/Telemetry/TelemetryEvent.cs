namespace Act.Core.Telemetry;

public sealed record TelemetryEvent(string Name, IReadOnlyDictionary<string, object?> Properties);
