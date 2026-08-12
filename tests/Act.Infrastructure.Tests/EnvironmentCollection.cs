namespace Act.Infrastructure.Tests;

// Environment variables are process-wide and xUnit runs test *classes* in parallel, so a class that
// sets `PATH` and a class that reads it are a race — one restores the value it captured and wins
// over whatever the other wrote. Every suite that touches the process environment joins this
// collection so they run one at a time.
//
// Found the hard way: `ExecutableProbeTests` read the ambient `PATH` and failed intermittently
// against `ExecutableResolverTests`, which replaces it.
[CollectionDefinition(Name)]
public sealed class EnvironmentCollection
{
    public const string Name = "process environment";
}
