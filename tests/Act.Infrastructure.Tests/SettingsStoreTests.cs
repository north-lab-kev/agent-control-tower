using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Load_returns_defaults_when_nothing_is_stored()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        var settings = provider.GetRequiredService<ISettingsStore>().Load();

        settings.Language.Should().Be(LanguagePreference.System);
        settings.Theme.Should().Be(ThemePreference.System);
        settings.Density.Should().Be(BoardDensity.Spacious);
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        using var temp = new TempDirectory();

        using (var writing = Provider(temp.Path))
        {
            writing.GetRequiredService<ISettingsStore>().Save(new UserSettings
            {
                Language = LanguagePreference.French,
                Theme = ThemePreference.Light,
                Density = BoardDensity.Compact,
            });
        }

        using var reading = Provider(temp.Path);

        var settings = reading.GetRequiredService<ISettingsStore>().Load();

        settings.Language.Should().Be(LanguagePreference.French);
        settings.Theme.Should().Be(ThemePreference.Light);
        settings.Density.Should().Be(BoardDensity.Compact);
    }

    [Fact]
    public void A_later_save_replaces_the_earlier_one()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ISettingsStore>();

        store.Save(new UserSettings { Theme = ThemePreference.Dark, Density = BoardDensity.Compact });
        store.Save(new UserSettings { Theme = ThemePreference.Light, Density = BoardDensity.Spacious });

        var settings = store.Load();

        settings.Theme.Should().Be(ThemePreference.Light);
        settings.Density.Should().Be(BoardDensity.Spacious);
    }

    [Fact]
    public void The_database_file_lands_in_the_data_directory()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);

        provider.GetRequiredService<ISettingsStore>().Save(new UserSettings());

        File.Exists(Path.Combine(temp.Path, "act.db")).Should().BeTrue();
    }

    [Fact]
    public void The_data_directory_is_created_on_first_use()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(Path.Combine(temp.Path, "nested"));

        provider.GetRequiredService<ISettingsStore>().Load();

        Directory.Exists(Path.Combine(temp.Path, "nested")).Should().BeTrue();
    }

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}
