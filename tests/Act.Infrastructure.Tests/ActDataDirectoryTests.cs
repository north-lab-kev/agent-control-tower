using Act.Infrastructure.Storage;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class ActDataDirectoryTests
{
    [Fact]
    public void Resolve_defaults_to_local_app_data_outside_the_installation_folder()
    {
        var directory = ActDataDirectory.Resolve();

        directory.Should().Be(LocalAppData("ACT"));
        directory.Should().NotStartWith(AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_keeps_the_plain_folder_for_the_installed_app(string? environment)
    {
        ActDataDirectory.Resolve(environment: environment).Should().Be(LocalAppData("ACT"));
    }

    [Fact]
    public void Resolve_isolates_a_non_production_environment_in_its_own_folder()
    {
        ActDataDirectory.Resolve(environment: "Development").Should().Be(LocalAppData("ACT.Development"));
    }

    [Fact]
    public void Resolve_ignores_the_environment_when_a_directory_is_configured()
    {
        using var temp = new TempDirectory();

        ActDataDirectory.Resolve(temp.Path, "Development").Should().Be(temp.Path);
    }

    [Fact]
    public void FolderFor_strips_characters_a_directory_name_cannot_hold()
    {
        ActDataDirectory.FolderFor("Dev/../Staging").Should().Be("ACT.Dev..Staging");
    }

    [Fact]
    public void FolderFor_falls_back_to_the_plain_folder_when_nothing_survives()
    {
        ActDataDirectory.FolderFor("//").Should().Be("ACT");
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

    private static string LocalAppData(string folder) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        folder);
}
