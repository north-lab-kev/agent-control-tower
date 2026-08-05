namespace Act.Infrastructure.Storage;

public static class ActDataDirectory
{
    public const string OverrideKey = "ACT_DATA_DIR";

    public const string ProductionEnvironment = "Production";

    private const string FolderName = "ACT";

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public static string Resolve(string? configured = null, string? environment = null)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                FolderFor(environment))
            : Path.GetFullPath(configured);

    public static string FolderFor(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment)
            || string.Equals(environment.Trim(), ProductionEnvironment, StringComparison.OrdinalIgnoreCase))
            return FolderName;

        var suffix = new string([.. environment.Trim().Where(c => !InvalidNameChars.Contains(c))]);

        return suffix.Length == 0 ? FolderName : $"{FolderName}.{suffix}";
    }
}
