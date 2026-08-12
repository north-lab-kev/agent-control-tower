using Act.Core.Abstractions;

namespace Act.App.UiTests;

// Nothing installed by default, which is the shape that makes the settings page show every warning it
// has. Name what should resolve and the probe answers for it.
internal sealed class FakeExecutableProbe : IExecutableProbe
{
    public HashSet<string> OnPathNames { get; } = [];

    public HashSet<string> Files { get; } = [];

    public Dictionary<string, string> Texts { get; } = [];

    public string? OnPath(string executable)
        => OnPathNames.Contains(executable) ? $"/usr/bin/{executable}" : null;

    public string? FirstExisting(IEnumerable<string> candidates) => candidates.FirstOrDefault(Files.Contains);

    public IReadOnlyList<string> DirectoriesNewestFirst(string parent) => [];

    public string? ReadText(string path) => Texts.GetValueOrDefault(path);
}
