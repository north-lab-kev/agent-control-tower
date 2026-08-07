namespace Act.Core.Abstractions;

// `PtyStartInfo` without the terminal, plus the two things a one-shot run needs and a session
// does not: what to write on standard input, and when to give up.
//
// `Input` is where every argument carrying arbitrary user text belongs. Both CLIs accept their
// prompt either positionally or on stdin, and stdin is the one route with no quoting rules to get
// wrong — it also survives a `.cmd` shim, where an argv full of quotes does not.
public sealed record CommandStartInfo(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDir,
    IReadOnlyDictionary<string, string> Environment,
    string? Input = null,
    TimeSpan? Timeout = null);
