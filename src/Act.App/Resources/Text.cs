using System.Globalization;

namespace Act.App.Resources;

internal static class Text
{
    public static string Format(string format, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, format, arguments);
}
