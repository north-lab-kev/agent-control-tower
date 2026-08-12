using System.Globalization;

namespace Act.App.Resources;

internal static class Text
{
    public static string Format(string format, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, format, arguments);

    // Which of a pair of wordings a count needs. Two forms is what English and French both take, and
    // the point of a pair at all is that `task(s)` reads as a string nobody finished — French is
    // worse off still, where the count also reaches the adjective and the verb.
    //
    // **Singular at one only, and zero belongs to the caller.** English wants "0 tasks" and French
    // wants "0 tâche", so a shared rule would have to be wrong in one of them; every count that
    // reaches here is one the caller has already established is not zero, because a phrase counting
    // nothing is a phrase not worth showing.
    public static string Plural(int count, string one, string many)
        => count is 1 ? one : many;

    // Token counts reach six and seven figures, and the exact digit never means anything — the
    // question is always "how much of a lot". Shared so the strip and the session rail round the
    // same number the same way; a card that disagreed with its own terminal would read as a bug.
    public static string Thousands(long value)
        => value >= 1000
            ? $"{value / 1000}k"
            : value.ToString(CultureInfo.CurrentCulture);
}
