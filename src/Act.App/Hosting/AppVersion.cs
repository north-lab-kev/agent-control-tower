using System.Reflection;

namespace Act.App.Hosting;

// What version ACT thinks it is, in the two forms anything needs it.
//
// Read off this assembly rather than the entry assembly, which under a test host is the test host.
// The two readers this replaced disagreed about that and about the `+<sha>` suffix; the updater made
// the disagreement matter, because the string it compares against a release tag has to be the same
// string the app reports.
public static class AppVersion
{
    // The whole informational version, build metadata included. For the log, where a commit hash is
    // exactly what you want when someone sends you a file and says it broke.
    public static string Full => Informational ?? Assembly().GetName().Version?.ToString() ?? Unknown;

    // Semver only — `1.2.3`, `1.2.3-beta.1` — with the `+<sha>` cut off. This is the form the
    // updater compares against a release and the form the UI shows: a hash is noise in both, and
    // `SemVer` would choke on it in neither, but the two would then disagree on screen.
    public static string Current => Trim(Informational) ?? Assembly().GetName().Version?.ToString() ?? Unknown;

    internal const string Unknown = "unknown";

    internal static string? Trim(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return null;

        var metadata = informational.IndexOf('+', StringComparison.Ordinal);

        return metadata < 0 ? informational : informational[..metadata];
    }

    private static string? Informational
        => Assembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static Assembly Assembly() => typeof(AppVersion).Assembly;
}
