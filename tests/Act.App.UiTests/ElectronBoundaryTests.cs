using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The one rule that keeps the desktop shell splittable: only the composition root and the
// quarantined `Desktop/` folder may name ElectronNET. It was a convention until this test; a
// convention is what a view reaches through the first time somebody needs a native dialog in a
// hurry, and by then browser mode is broken in a way nothing catches.
public class ElectronBoundaryTests
{
    private static readonly string[] Allowed =
    [
        "Program.cs",
        "ServiceCollectionExtensions.cs",
    ];

    [Fact]
    public void Only_the_composition_root_and_the_shell_name_electron()
    {
        var app = AppSourceRoot();

        var offenders =
            from file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            let relative = Path.GetRelativePath(app, file)
            where !relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith("Desktop" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !Allowed.Contains(relative)
            where File.ReadAllText(file).Contains("ElectronNET", StringComparison.Ordinal)
            select relative;

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_shell_folder_is_where_it_actually_lives()
    {
        var shell = Path.Combine(AppSourceRoot(), "Desktop", "DesktopShell.cs");

        File.ReadAllText(shell).Should().Contain("ElectronNET");
    }

    private static string AppSourceRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Act.App"));
}
