using Act.Core.Abstractions;

namespace Act.TestSupport;

// Generated agent config, kept in memory. The point is the same as `StubPtyHost`'s: the adapter
// contract suite asserts what a launch would write without writing anything, so it stays as fast
// and as side-effect-free as the rest of the fast suite.
public sealed class StubAgentConfigFiles : IAgentConfigFiles
{
    public Dictionary<string, string> Written { get; } = [];

    public Dictionary<string, string> External { get; } = [];

    public List<Guid> Cleared { get; } = [];

    public string Write(Guid taskId, string fileName, string content)
    {
        var path = Path.Combine(Root(taskId), fileName);

        Written[path] = content;

        return path;
    }

    public string WriteShared(string fileName, string content)
    {
        var path = Path.Combine(SharedRoot, fileName);

        Written[path] = content;

        return path;
    }

    public void WriteExternal(string absolutePath, string content) => External[absolutePath] = content;

    // Named, not created: nothing runs in the fast suite, so a real empty directory would be a
    // side effect bought for an assertion about a string.
    public string ScratchDirectory() => Path.Combine(SharedRoot, "scratch");

    public void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker)
    {
        var tail = External.TryGetValue(absolutePath, out var existing)
            ? Tail(existing, tailMarker)
            : null;

        if (tail is not null)
            Preserved.Add(absolutePath);

        WriteExternal(absolutePath, tail is null ? content : content + Environment.NewLine + tail);
    }

    public List<string> Preserved { get; } = [];

    private static string? Tail(string existing, string tailMarker)
    {
        var lines = existing.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(
            lines,
            line => line.TrimStart().StartsWith(tailMarker, StringComparison.Ordinal));

        return start < 0 ? null : string.Join(Environment.NewLine, lines[start..]);
    }

    public void DeleteExternal(string absolutePath) => External.Remove(absolutePath);

    public void Clear(Guid taskId)
    {
        Cleared.Add(taskId);

        foreach (var path in Written.Keys.Where(key => key.StartsWith(Root(taskId), StringComparison.Ordinal)).ToList())
            Written.Remove(path);
    }

    public string? Content(string fileName)
        => Written.FirstOrDefault(entry => Path.GetFileName(entry.Key) == fileName).Value;

    private static string SharedRoot => Path.Combine(Path.GetTempPath(), "act-tests");

    private static string Root(Guid taskId) => Path.Combine(SharedRoot, taskId.ToString("d"));
}
