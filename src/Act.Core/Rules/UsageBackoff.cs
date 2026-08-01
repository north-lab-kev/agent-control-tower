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
        => Backs(availability) ? Math.Min(failures + 1, MostDoublings) : 0;

    public static TimeSpan Delay(TimeSpan poll, int failures)
    {
        if (failures <= 0)
            return poll;

        var ceiling = poll > Ceiling ? poll : Ceiling;
        var scaled = poll * Math.Pow(2, Math.Min(failures, MostDoublings));

        return scaled > ceiling ? ceiling : scaled;
    }
}
