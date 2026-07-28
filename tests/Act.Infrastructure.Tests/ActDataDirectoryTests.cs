using Act.Infrastructure.Storage;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class ActDataDirectoryTests
{
    [Fact]
    public void Resolve_defaults_to_local_app_data_outside_the_installation_folder()
    {
        var directory = ActDataDirectory.Resolve();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACT");

        directory.Should().Be(expected);
        directory.Should().NotStartWith(AppContext.BaseDirectory);
    }

    [Fact]
    public void Resolve_uses_the_configured_directory()
    {
        using var temp = new TempDirectory();

        ActDataDirectory.Resolve(temp.Path).Should().Be(temp.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_falls_back_to_the_default_when_the_configured_value_is_blank(string configured)
    {
        ActDataDirectory.Resolve(configured).Should().Be(ActDataDirectory.Resolve());
    }

    [Fact]
    public void Resolve_expands_a_relative_configured_directory()
    {
        var directory = ActDataDirectory.Resolve(Path.Combine(".", "act-data"));

        Path.IsPathFullyQualified(directory).Should().BeTrue();
        directory.Should().EndWith("act-data");
    }

    [Fact]
    public void Resolve_does_not_create_anything()
    {
        using var temp = new TempDirectory();

        ActDataDirectory.Resolve(temp.Path);

        Directory.Exists(temp.Path).Should().BeFalse();
    }
}
