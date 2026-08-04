namespace Act.Infrastructure.Logging;

public static class ActLogDirectory
{
    private const string FolderName = "logs";

    public static string Resolve(string dataDirectory) => Path.Combine(dataDirectory, FolderName);
}
