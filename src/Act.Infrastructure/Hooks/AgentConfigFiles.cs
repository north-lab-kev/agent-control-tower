using System.Text;
using Act.Core.Abstractions;

namespace Act.Infrastructure.Hooks;

public sealed class AgentConfigFiles(string dataDirectory) : IAgentConfigFiles
{
    public const string DirectoryName = "agent-config";

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

    public void WriteExternal(string absolutePath, string content)
    {
        var directory = Path.GetDirectoryName(absolutePath);

        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        File.WriteAllText(absolutePath, content, Utf8);
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
