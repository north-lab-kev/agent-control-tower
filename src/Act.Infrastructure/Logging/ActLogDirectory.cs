namespace Act.Infrastructure.Logging;

internal static class ActLogDirectory
{
    private const string FolderName = "logs";

    public static string Resolve(string dataDirectory) => Path.Combine(dataDirectory, FolderName);
}
