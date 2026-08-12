namespace Act.Core.Abstractions;

// Both streams, always, and separately — never merged. Codex writes its banner, the echoed
// prompt and its token count to standard error and only the final message to standard output, so
// merging the two would turn a clean one-line answer into a transcript to parse. `Error` is kept
// because it is what a failure says, and a caller that has nothing to show the user still has
// something to log.
//
// `TimedOut` is distinct from a non-zero exit: a CLI that was killed for taking too long said
// nothing about the request, whereas one that exited 1 usually did.
public sealed record CommandResult(int ExitCode, string Output, string Error, bool TimedOut = false)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
