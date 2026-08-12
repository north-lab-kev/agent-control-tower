using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The splash is package-driven: `ElectronSplashScreen` in the csproj flows into the Electron
// host's manifest, and the host shows the image at `ready` — before the .NET side exists. Nothing
// compiles against the file, so the only thing standing between a rename and a silently
// splash-less build is this test.
public class SplashScreenTests
{
    private const int MinWindowWidth = 1000;

    private const int MinWindowHeight = 320;

    [Fact]
    public void The_project_declares_the_splash_screen()
    {
        var project = File.ReadAllText(Path.Combine(AppSourceRoot(), "Act.App.csproj"));

        project.Should().Contain("<ElectronSplashScreen>splash.png</ElectronSplashScreen>");
    }

    [Fact]
    public void The_splash_image_exists_where_the_project_points()
    {
        File.Exists(SplashPath()).Should().BeTrue();
    }

    [Fact]
    public void The_splash_is_a_png_no_larger_than_the_smallest_screen_the_app_accepts()
    {
        var header = new byte[24];

        using (var stream = File.OpenRead(SplashPath()))
            stream.ReadExactly(header);

        header.Take(8).Should().Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));

        width.Should().BeInRange(1, MinWindowWidth);
        height.Should().BeInRange(1, MinWindowHeight);
    }

    private static string SplashPath() => Path.Combine(AppSourceRoot(), "splash.png");

    private static string AppSourceRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Act.App"));
}
