namespace Act.Core.Abstractions;

// A pseudo-terminal has to be sized at spawn, which is why `Size` travels with the launch
// rather than being applied afterwards: the agent reads the window size on start-up and
// lays its TUI out once from it.
public sealed record PtyStartInfo(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDir,
    IReadOnlyDictionary<string, string> Environment,
    TerminalSize Size);
