namespace Act.Core.Abstractions;

// Read-only questions about the filesystem, for adapters working out where their CLI is installed.
// The split is deliberate: the **adapter** knows where to look — that is agent knowledge and must
// not leak into infrastructure — while this port knows how to look, which is the only part that
// touches the OS. It also makes discovery testable without installing anything.
public interface IExecutableProbe
{
    // The full path a bare name resolves to on `PATH` (honouring `PATHEXT` on Windows), or null.
    string? OnPath(string executable);

    // The first candidate that exists, so an adapter can list its install shapes in preference
    // order and let the probe pick.
    string? FirstExisting(IEnumerable<string> candidates);

    // Immediate subdirectories, newest first. Codex installs under a build-hash folder, so the
    // path cannot be written out in full — and after an upgrade there is more than one.
    IReadOnlyList<string> DirectoriesNewestFirst(string parent);

    // Null when the file is missing or unreadable. For the pointer files an installer leaves
    // behind, such as Codex's `CODEX_CLI_PATH`.
    string? ReadText(string path);
}
