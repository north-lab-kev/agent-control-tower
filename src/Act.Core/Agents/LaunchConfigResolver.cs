using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Core.Agents;

// The never-silently-drop rule, once, for any adapter whose capabilities describe it fully.
// The asymmetry between model and effort is deliberate: a wrong model changes what you are
// billed for and what the answer is worth, so it is refused; effort is advisory everywhere it
// exists, so it is substituted and recorded rather than blocking a launch over.
public static class LaunchConfigResolver
{
    public static LaunchConfigResolution Resolve(
        AgentType agent,
        AgentCapabilities capabilities,
        LaunchConfig config)
    {
        var resolved = config.Copy();
        var adjustments = new List<LaunchConfigAdjustment>();
        var rejections = new List<string>();

        resolved.Model ??= capabilities.DefaultModel;

        if (resolved.Model is { } model && capabilities.Model(model) is null)
            rejections.Add($"Model '{model}' is not available for {agent}.");

        ResolveEffort(agent, capabilities, resolved, adjustments);

        if (!capabilities.PermissionModes.Contains(resolved.PermissionMode))
            rejections.Add($"Permission mode '{resolved.PermissionMode}' is not supported by {agent}.");

        return new LaunchConfigResolution(resolved, adjustments, rejections);
    }

    private static void ResolveEffort(
        AgentType agent,
        AgentCapabilities capabilities,
        LaunchConfig resolved,
        List<LaunchConfigAdjustment> adjustments)
    {
        var efforts = capabilities.EffortsFor(resolved.Model);

        // An empty ladder is how a model says it does not model effort at all, in which case
        // handing the requested value straight through is the honest outcome.
        if (efforts.Count == 0)
            return;

        if (resolved.Effort is null)
        {
            resolved.Effort = capabilities.Model(resolved.Model)?.DefaultEffort;

            return;
        }

        if (efforts.Contains(resolved.Effort))
            return;

        var requested = resolved.Effort;
        var substitute = capabilities.Model(resolved.Model)?.DefaultEffort is { } fallback
            && efforts.Contains(fallback)
                ? fallback
                : efforts[^1];

        adjustments.Add(new LaunchConfigAdjustment(
            nameof(LaunchConfig.Effort),
            requested,
            substitute,
            $"'{requested}' is not one of the reasoning efforts {resolved.Model} accepts on {agent}."));

        resolved.Effort = substitute;
    }
}
