using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using LiteDB;
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
        settings.Density.Should().Be(BoardDensity.Detailed);
        settings.AutoArchiveCompleted.Should().BeTrue();
        settings.AutoArchiveCompletedAfterDays.Should().Be(10);
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
                AutoArchiveCompleted = false,
                AutoArchiveCompletedAfterDays = 45,
            });
        }

        using var reading = Provider(temp.Path);

        var settings = reading.GetRequiredService<ISettingsStore>().Load();

        settings.Language.Should().Be(LanguagePreference.French);
        settings.Theme.Should().Be(ThemePreference.Light);
        settings.Density.Should().Be(BoardDensity.Compact);
        settings.AutoArchiveCompleted.Should().BeFalse();
        settings.AutoArchiveCompletedAfterDays.Should().Be(45);
    }

    [Fact]
    public void A_later_save_replaces_the_earlier_one()
    {
        using var temp = new TempDirectory();
        using var provider = Provider(temp.Path);
        var store = provider.GetRequiredService<ISettingsStore>();

        store.Save(new UserSettings { Theme = ThemePreference.Dark, Density = BoardDensity.Compact });
        store.Save(new UserSettings { Theme = ThemePreference.Light, Density = BoardDensity.Detailed });

        var settings = store.Load();

        settings.Theme.Should().Be(ThemePreference.Light);
        settings.Density.Should().Be(BoardDensity.Detailed);
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

    // `Spacious` was renamed to `Detailed`, and the enum is persisted by name — so every settings
    // document written before the rename carries a name this build does not have. Reading it back as
    // the default is what keeps such a machine able to start at all.
    [Fact]
    public void A_density_name_this_build_retired_loads_as_the_default()
    {
        using var temp = new TempDirectory();

        using (var provider = Provider(temp.Path))
        {
            provider.GetRequiredService<ISettingsStore>().Save(new UserSettings { Language = LanguagePreference.French });
        }

        using (var database = new LiteDatabase(Path.Combine(temp.Path, "act.db")))
        {
            var settings = database.GetCollection("settings");
            var document = settings.FindById(1);

            document["Settings"]["Density"] = "Spacious";

            settings.Update(document);
        }

        using var reading = Provider(temp.Path);

        var stored = reading.GetRequiredService<ISettingsStore>().Load();

        stored.Density.Should().Be(BoardDensity.Detailed);
        stored.Language.Should().Be(LanguagePreference.French);
    }

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}
