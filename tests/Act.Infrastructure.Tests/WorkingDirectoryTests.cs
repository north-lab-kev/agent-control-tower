using Act.Infrastructure.FileSystem;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class WorkingDirectoryTests
{
    private static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    // The case that actually bit: the new-task form's own placeholder is `~/dev/act`, and nothing
    // below a shell expands it.
    [Fact]
    public void A_tilde_path_expands_to_the_home_directory()
        => WorkingDirectory.Resolve("~/dev/act")
            .Should().Be(Path.Combine(Home, "dev", "act"));

    [Fact]
    public void A_backslash_tilde_path_expands_too()
        => WorkingDirectory.Resolve(@"~\dev\act")
            .Should().Be(Path.Combine(Home, "dev", "act"));

    [Fact]
    public void A_bare_tilde_is_the_home_directory()
        => WorkingDirectory.Resolve("~").Should().Be(Path.GetFullPath(Home));

    // Only a leading `~` is a home reference; one in the middle is an ordinary character, and a
    // directory may legitimately be called that.
    [Fact]
    public void A_tilde_inside_a_path_is_left_alone()
    {
        var path = Path.Combine(Path.GetTempPath(), "a~b");

        WorkingDirectory.Resolve(path).Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void An_absolute_path_survives_unchanged()
    {
        var path = Path.GetFullPath(Path.GetTempPath());

        WorkingDirectory.Resolve(path).Should().Be(path);
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
        => WorkingDirectory.Resolve("  ~/dev  ").Should().Be(Path.Combine(Home, "dev"));

    [Fact]
    public void An_empty_path_falls_back_to_the_current_directory()
        => WorkingDirectory.Resolve("   ").Should().Be(Directory.GetCurrentDirectory());
}
