namespace Act.Core.Abstractions;

// Spawning a process under a pseudo-terminal is agent-agnostic plumbing — only the command
// line is agent-shaped — so it is a port here and an implementation in infrastructure, and
// adapters take it by injection rather than referencing infrastructure sideways.
public interface IPtyHost
{
    Task<IPtyProcess> StartAsync(PtyStartInfo startInfo, CancellationToken cancellationToken = default);
}
