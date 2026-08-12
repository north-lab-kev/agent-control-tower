using System.Text;
using Act.Core.Abstractions;

namespace Act.Infrastructure.Hooks;

public sealed class AgentConfigFiles(string dataDirectory) : IAgentConfigFiles
{
    public const string DirectoryName = "agent-config";

    public const string ScratchName = "scratch";

    // No BOM, deliberately and everywhere: Codex's hooks json parser rejects one outright
    // ("expected value at line 1 column 1"), and nothing else here wants it either.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string Write(Guid taskId, string fileName, string content)
        => WriteAt(Path.Combine(dataDirectory, DirectoryName, taskId.ToString("d")), fileName, content);

    public string WriteShared(string fileName, string content)
        => WriteAt(Path.Combine(dataDirectory, DirectoryName), fileName, content);

    private static string WriteAt(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);

        File.WriteAllText(path, content, Utf8);

        return path;
    }

    // Created rather than only named: a CLI handed a working directory that does not exist fails at
    // spawn. Nothing is ever written into it — being empty is the whole of what it is for.
    public string ScratchDirectory()
    {
        var directory = Path.Combine(dataDirectory, ScratchName);

        Directory.CreateDirectory(directory);

        return directory;
    }

    public void WriteExternal(string absolutePath, string content)
    {
        var directory = Path.GetDirectoryName(absolutePath);

        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        File.WriteAllText(absolutePath, content, Utf8);
    }

    // The tail is what the CLI wrote after ACT's marker, minus any table the fresh content defines
    // again. Codex reorders the file when it saves — measured in `docs/findings/codex-hooks.md`, the
    // `[mcp_servers.act]` block came back *below* `[hooks.state]` — so a verbatim tail would carry
    // ACT's own table back in under the fresh copy, and a table defined twice is a profile the
    // parser rejects: every later launch would die on it.
    public void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker)
    {
        var tail = Tail(absolutePath, tailMarker, TableNames(content));

        WriteExternal(absolutePath, tail is null ? content : content + Environment.NewLine + tail);
    }

    private static string? Tail(string absolutePath, string tailMarker, HashSet<string> ownTables)
    {
        try
        {
            if (!File.Exists(absolutePath))
                return null;

            var lines = File.ReadAllLines(absolutePath, Utf8);
            var start = Array.FindIndex(
                lines,
                line => line.TrimStart().StartsWith(tailMarker, StringComparison.Ordinal));

            if (start < 0)
                return null;

            var kept = new List<string>();
            var dropping = false;

            foreach (var line in lines[start..])
            {
                if (TableName(line) is { } table)
                    dropping = ownTables.Contains(table);

                if (!dropping)
                    kept.Add(line);
            }

            return kept.Count == 0 ? null : string.Join(Environment.NewLine, kept);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HashSet<string> TableNames(string content)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in content.Split('\n'))
        {
            if (TableName(line) is { } name)
                names.Add(name);
        }

        return names;
    }

    private static string? TableName(string line)
    {
        var trimmed = line.TrimStart();

        if (!trimmed.StartsWith('['))
            return null;

        var name = trimmed.TrimStart('[');
        var close = name.IndexOf(']');

        return close < 0 ? null : name[..close].Trim();
    }

    public void DeleteExternal(string absolutePath)
    {
        try
        {
            File.Delete(absolutePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear(Guid taskId)
    {
        var directory = Path.Combine(dataDirectory, DirectoryName, taskId.ToString("d"));

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
