using Act.Core.Spawning;

namespace Act.App.Cards;

// Either the card that was made or the reasons it was not — never both, and never neither. The
// refusals are text an *agent* reads and retries against, so they name what it should have said
// rather than describing ACT's internals.
public sealed record FollowUpOutcome(FollowUpCreated? Created, IReadOnlyList<string> Refusals)
{
    public static FollowUpOutcome Ok(FollowUpCreated created) => new(created, []);

    public static FollowUpOutcome Refused(params string[] refusals) => new(null, refusals);
}
