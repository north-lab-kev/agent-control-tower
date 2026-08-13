using System.Runtime.CompilerServices;

namespace Act.App.UiTests;

internal static class ReleaseWorkflowFile
{
    public static string Text()
        => File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

    public static string RepositoryRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
