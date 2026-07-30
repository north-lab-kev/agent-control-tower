namespace Act.Core.Agents;

// The agent → ACT file contract in one place: where the status and follow-up files live,
// and how their names are formed. Everything that later writes, watches, ingests or cleans
// these paths resolves them here instead of rebuilding strings — the preamble that teaches
// the convention is generated from the same constants.
public static class ActContract
{
    public const string RootDirectoryName = ".act";

    public const string StatusDirectoryName = "status";

    public const string FollowUpsDirectoryName = "followups";

    public const string ConsumedDirectoryName = "consumed";

    public const string RelativeStatusDirectory = $"{RootDirectoryName}/{StatusDirectoryName}";

    public const string RelativeFollowUpsDirectory = $"{RootDirectoryName}/{FollowUpsDirectoryName}";

    public static string RootDirectory(string workingDir)
        => Path.Combine(workingDir, RootDirectoryName);

    public static string StatusDirectory(string workingDir)
        => Path.Combine(RootDirectory(workingDir), StatusDirectoryName);

    public static string FollowUpsDirectory(string workingDir)
        => Path.Combine(RootDirectory(workingDir), FollowUpsDirectoryName);

    public static string ConsumedDirectory(string workingDir)
        => Path.Combine(FollowUpsDirectory(workingDir), ConsumedDirectoryName);

    // ACT owns the prefix and the agent fills the suffix, so one `.act/` directory shared by
    // several tasks in the same repository can never mix their files up.
    public static string FilePrefix(Guid taskId) => $"{taskId:d}-";

    public static string FilePattern(Guid taskId) => $"{FilePrefix(taskId)}*.json";

    public static string StatusFileName(Guid taskId, int turn) => $"{FilePrefix(taskId)}{turn}.json";

    public static bool BelongsToTask(string fileName, Guid taskId)
        => Path.GetFileName(fileName).StartsWith(FilePrefix(taskId), StringComparison.OrdinalIgnoreCase);
}
