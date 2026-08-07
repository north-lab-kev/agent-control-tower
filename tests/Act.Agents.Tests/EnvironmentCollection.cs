namespace Act.Agents.Tests;

// `CODEX_HOME` is process-wide and xUnit runs test *classes* in parallel, so two suites that set it
// around an assertion would each restore the value they captured and clobber the other. Every suite
// that touches the process environment joins this collection so they run one at a time.
[CollectionDefinition(Name)]
public sealed class EnvironmentCollection
{
    public const string Name = "process environment";
}
