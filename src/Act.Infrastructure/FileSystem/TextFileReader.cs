using Act.Core.Abstractions;

namespace Act.Infrastructure.FileSystem;

public sealed class TextFileReader : ITextFileReader
{
    public string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
