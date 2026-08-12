using Act.Core.Abstractions;

namespace Act.TestSupport;

// `StubPtyHost` for the other kind of start: it records the command line a query would have run and
// hands back whatever the test wants the CLI to have said. Nothing is executed, which is what lets
// the adapter suites assert the flags of a one-shot run on a machine with no CLI installed.
public sealed class StubCommandHost : ICommandHost
{
    public List<CommandStartInfo> Ran { get; } = [];

    public CommandStartInfo Last => Ran[^1];

    public CommandResult Result { get; set; } = new(0, "Stub answer", string.Empty);

    public Exception? Fails { get; set; }

    public Task<CommandResult> RunAsync(
        CommandStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        Ran.Add(startInfo);

        if (Fails is { } failure)
            throw failure;

        return Task.FromResult(Result);
    }
}
