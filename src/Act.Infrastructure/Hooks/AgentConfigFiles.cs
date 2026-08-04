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

    public void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker)
    {
        var tail = Tail(absolutePath, tailMarker);

        WriteExternal(absolutePath, tail is null ? content : content + Environment.NewLine + tail);
    }

    private static string? Tail(string absolutePath, string tailMarker)
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

            return string.Join(Environment.NewLine, lines[start..]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
