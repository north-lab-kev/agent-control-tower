namespace Act.Core.Abstractions;

// Malformed and merely-missing are different answers and the UI treats them differently: a bad
// path is an error that blocks the save, a missing one is a warning with an offer to create it.
// Collapsing them into a bool would lose that.
public sealed record PathCheck(bool WellFormed, bool Exists, string Resolved, string? Error)
{
    public static PathCheck Malformed(string error) => new(false, false, string.Empty, error);

    public static PathCheck Missing(string resolved) => new(true, false, resolved, null);

    public static PathCheck Found(string resolved) => new(true, true, resolved, null);
}

// Keys rather than sentences: the UI localizes them, and infrastructure has no business
// composing user-facing prose. Beside `PathCheck` because they are the vocabulary of its `Error`,
// and a UI reading one must not have to reference infrastructure to name the other.
public static class PathError
{
    public const string Empty = "empty";

    public const string NotAbsolute = "not-absolute";

    public const string Malformed = "malformed";

    public const string Missing = "missing";

    public const string Unreadable = "unreadable";
}
