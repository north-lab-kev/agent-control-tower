namespace Act.Core.Telemetry;

// The second belt. `TelemetryEvents` is the only place a payload is built, so nothing free-form
// should ever reach here — this is what makes that a fact rather than a convention: an event whose
// name is not declared never leaves, a property whose key is not declared never leaves, and a value
// that is not a bounded scalar never leaves.
//
// The forbidden characters are chosen for what they mark rather than for what they are: a path
// separator, a home shortcut, a quote or a newline is what tells a leaked prompt, path or command
// line apart from the version strings and enum names this is meant to carry. See *What telemetry
// may carry* in `docs/design-notes.md`.
public static class TelemetryPayload
{
    public const int LongestSymbol = 200;

    private static readonly char[] NeverInASymbol = ['/', '\\', '~', '"', '\'', '\r', '\n', '\t'];

    private static readonly HashSet<string> Names = new(TelemetryEvents.Names.All, StringComparer.Ordinal);

    private static readonly HashSet<string> Keys = new(TelemetryProperties.All, StringComparer.Ordinal);

    public static TelemetryEvent? Sanitize(TelemetryEvent telemetry)
    {
        if (!Names.Contains(telemetry.Name))
            return null;

        var kept = new Dictionary<string, object?>(telemetry.Properties.Count, StringComparer.Ordinal);

        foreach (var property in telemetry.Properties)
        {
            if (!Keys.Contains(property.Key))
                continue;

            if (Allowed(property.Value) is { } value)
                kept[property.Key] = value;
        }

        return new TelemetryEvent(telemetry.Name, kept);
    }

    public static bool IsSymbol(string? text)
        => text is { Length: > 0 and <= LongestSymbol }
            && text.IndexOfAny(NeverInASymbol) < 0;

    private static object? Allowed(object? value) => value switch
    {
        bool or int or long or double => value,
        Enum symbol => symbol.ToString(),
        string text => IsSymbol(text) ? text : null,
        IEnumerable<string> items => Symbols(items),
        _ => null,
    };

    private static object? Symbols(IEnumerable<string> items)
    {
        string[] kept = [.. items.Where(IsSymbol).Take(TelemetryFault.MostFrames)];

        return kept.Length > 0 ? kept : null;
    }
}
