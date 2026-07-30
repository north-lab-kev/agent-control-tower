using Act.Core.Model;

namespace Act.Core.Abstractions;

// The never-silently-drop rule made mechanical: an unsupported value is either substituted
// with a recorded adjustment or rejected with a message the user can read. There is no
// third outcome, so nothing an adapter cannot honour can vanish unnoticed.
public sealed record LaunchConfigResolution(
    LaunchConfig Resolved,
    IReadOnlyList<LaunchConfigAdjustment> Adjustments,
    IReadOnlyList<string> Rejections)
{
    public bool CanLaunch => Rejections.Count == 0;

    public static LaunchConfigResolution Accepted(LaunchConfig config) => new(config, [], []);
}
