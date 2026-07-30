namespace Act.Infrastructure.Storage;

public static class ActDataDirectory
{
    public const string OverrideKey = "ACT_DATA_DIR";

    private const string FolderName = "ACT";

    public static string Resolve(string? configured = null)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                FolderName)
            : Path.GetFullPath(configured);
}
