using Act.Core.Model;

namespace Act.Core.Rules;

public static class UsageWake
{
    public static readonly TimeSpan Floor = TimeSpan.FromSeconds(60);

    public static bool Wakes(UsageAvailability availability)
        => availability is UsageAvailability.NotSignedIn
            or UsageAvailability.Expired
            or UsageAvailability.SignInRequired
            or UsageAvailability.Unauthorized;
}
