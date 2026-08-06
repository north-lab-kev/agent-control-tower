using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Core.Spawning;

// The same never-silently-drop shape `LaunchConfigResolution` has, for the same reason: a value the
// agent asked for is either honoured, substituted with a recorded adjustment, or refused with a
// message it can read and retry against. There is no fourth outcome, and `Card` is null exactly
// when `Rejections` is not empty.
public sealed record FollowUpResolution(
    Card? Card,
    IReadOnlyList<LaunchConfigAdjustment> Adjustments,
    IReadOnlyList<string> Rejections)
{
    public bool CanCreate => Rejections.Count == 0 && Card is not null;

    public static FollowUpResolution Refused(params string[] rejections) => new(null, [], rejections);
}
