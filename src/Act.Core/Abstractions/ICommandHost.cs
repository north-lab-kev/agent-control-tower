namespace Act.Core.Abstractions;

// The second way ACT starts a process, and the opposite of `IPtyHost` in every respect that
// matters: no terminal, no session, no streaming — one short-lived run whose whole output is its
// return value. It exists for the things ACT asks a CLI *for itself* rather than on the user's
// behalf, where a pty would be both wrong and unreadable, because scraping a TUI's paint is not
// reading an answer.
//
// A port for the same two reasons `IPtyHost` is one: the process table is infrastructure, and the
// adapter suites have to be able to assert a command line without a CLI on the machine.
public interface ICommandHost
{
    Task<CommandResult> RunAsync(CommandStartInfo startInfo, CancellationToken cancellationToken = default);
}
