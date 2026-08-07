using System.Globalization;

namespace Act.App.Resources;

internal static class Text
{
    public static string Format(string format, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, format, arguments);

    // Token counts reach six and seven figures, and the exact digit never means anything — the
    // question is always "how much of a lot". Shared so the strip and the session rail round the
    // same number the same way; a card that disagreed with its own terminal would read as a bug.
    public static string Thousands(long value)
        => value >= 1000
            ? $"{value / 1000}k"
            : value.ToString(CultureInfo.CurrentCulture);
}
