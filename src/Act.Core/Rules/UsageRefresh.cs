using Act.Core.Model;

namespace Act.Core.Rules;

public static class UsageRefresh
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan Ceiling = TimeSpan.FromHours(2);

    public const int MostDoublings = 5;

    public static bool Answers(UsageAvailability availability)
        => availability is UsageAvailability.Expired;

    public static bool Due(DateTimeOffset now, DateTimeOffset? attempted, TimeSpan cooldown)
        => attempted is not { } last || now - last >= cooldown;

    // Counted on the reading *after* the nudge: a run that left the token expired spent the user's
    // tokens and achieved nothing, and the likeliest cause — a refresh token revoked server-side
    // while its stated expiry is still in the future — does not heal on its own. Unlike
    // `UsageBackoff`, which throttles requests to someone else's endpoint, this throttles spending.
    public static int Count(UsageAvailability availability, int failures)
        => Answers(availability) ? Math.Min(failures + 1, MostDoublings) : 0;

    public static TimeSpan Delay(TimeSpan cooldown, int failures)
    {
        if (failures <= 0)
            return cooldown;

        var ceiling = cooldown > Ceiling ? cooldown : Ceiling;
        var scaled = cooldown * Math.Pow(2, Math.Min(failures, MostDoublings));

        return scaled > ceiling ? ceiling : scaled;
    }
}
