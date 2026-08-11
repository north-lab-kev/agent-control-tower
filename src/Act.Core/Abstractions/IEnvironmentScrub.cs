namespace Act.Core.Abstractions;

// What one agent refuses to inherit from the environment ACT itself was launched with. Which
// variables those are is a fact about a single CLI, so it belongs to that CLI's adapter;
// `AgentEnvironment` knows only that there is a point at which they go — before ACT's own variables
// and before the install's overrides, so both of those still win.
public interface IEnvironmentScrub
{
    void Apply(IDictionary<string, string> environment);
}
