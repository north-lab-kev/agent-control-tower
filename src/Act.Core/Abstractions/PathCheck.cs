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
