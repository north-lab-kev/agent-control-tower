using Act.Core.Model;

namespace Act.Core.Rules;

public static class UsageRefresh
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    public static bool Answers(UsageAvailability availability)
        => availability is UsageAvailability.Expired;

    public static bool Due(DateTimeOffset now, DateTimeOffset? attempted, TimeSpan cooldown)
        => attempted is not { } last || now - last >= cooldown;
}
