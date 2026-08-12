using Act.Core.Model;

namespace Act.Core.Rules;

public static class UsageBackoff
{
    public static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(15);

    public const int MostDoublings = 5;

    public static bool Backs(UsageAvailability availability)
        => availability is UsageAvailability.Unauthorized
            or UsageAvailability.Unreachable
            or UsageAvailability.Failed;

    public static int Count(UsageAvailability availability, int failures)
        => Backs(availability) ? Bounded(failures) : 0;

    public static TimeSpan Delay(TimeSpan poll, int failures) => Delay(poll, failures, Ceiling);

    // The one exponential both usage throttles share, parameterized on the ceiling: `UsageRefresh`
    // calls it with its own, so a correction to the math cannot land in one and not the other. The
    // floor wins over a ceiling it already exceeds — a daily poll must not be shortened by backoff.
    internal static TimeSpan Delay(TimeSpan floor, int failures, TimeSpan ceiling)
    {
        if (failures <= 0)
            return floor;

        var cap = floor > ceiling ? floor : ceiling;
        var scaled = floor * Math.Pow(2, Math.Min(failures, MostDoublings));

        return scaled > cap ? cap : scaled;
    }

    internal static int Bounded(int failures) => Math.Min(failures + 1, MostDoublings);
}
