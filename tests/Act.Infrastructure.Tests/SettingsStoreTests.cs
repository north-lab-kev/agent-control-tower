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

    // Templates are stored as a list of objects that each carry an `Id`, which LiteDB maps to `_id`
    // even nested — so the one thing worth pinning is that a template comes back as the same template,
    // since a lost id is a template the New task picker can name but never resolve.
    [Fact]
    public void A_template_survives_a_restart_intact()
    {
        using var temp = new TempDirectory();

        var id = Guid.NewGuid();

        using (var writing = Provider(temp.Path))
        {
            writing.GetRequiredService<ISettingsStore>().Save(new UserSettings
            {
                Templates =
                [
                    new TaskTemplate
                    {
                        Id = id,
                        IsDefault = true,
                        Name = "Bugfix",
                        Title = "Fix the failing test",
                        Prompt = "Find and fix the failing test",
                        WorkingDir = "C:/dev/act",
                        Agent = AgentType.Codex,
                        Model = "gpt-5.6-terra",
                        Effort = "high",
                        PermissionMode = PermissionMode.AcceptEdits,
                        Schedule = TaskSchedule.NextWindow,
                        AllowConcurrentWorkingDir = true,
                    },
                ],
            });
        }

        using var reading = Provider(temp.Path);

        var template = reading.GetRequiredService<ISettingsStore>().Load().Templates.Should().ContainSingle().Subject;

        template.Id.Should().Be(id);
        template.IsDefault.Should().BeTrue();
        template.Name.Should().Be("Bugfix");
        template.Title.Should().Be("Fix the failing test");
        template.Prompt.Should().Be("Find and fix the failing test");
        template.WorkingDir.Should().Be("C:/dev/act");
        template.Agent.Should().Be(AgentType.Codex);
        template.Model.Should().Be("gpt-5.6-terra");
        template.Effort.Should().Be("high");
        template.PermissionMode.Should().Be(PermissionMode.AcceptEdits);
        template.Schedule.Should().Be(TaskSchedule.NextWindow);
        template.AllowConcurrentWorkingDir.Should().BeTrue();
    }

    // `BsonMapper.EmptyStringToNull` defaults to `true`, which wrote every empty string to disk as
    // null and handed a non-nullable property back as null on the way out — see *Empty strings were
    // stored as null* in `docs/design-notes.md`. `ActBsonMapper` turns it off, and nothing in either
    // type's declaration says so, which is why the round trip is pinned here.
    [Fact]
    public void An_empty_string_survives_a_restart()
    {
        using var temp = new TempDirectory();

        using (var writing = Provider(temp.Path))
        {
            writing.GetRequiredService<ISettingsStore>().Save(new UserSettings
            {
                Templates = [new TaskTemplate { Id = Guid.NewGuid(), IsDefault = true }],
            });
        }

        using var reading = Provider(temp.Path);

        var template = reading.GetRequiredService<ISettingsStore>().Load()
            .Templates.Should().ContainSingle().Subject;

        template.Name.Should().BeEmpty();
        template.Title.Should().BeEmpty();
        template.Prompt.Should().BeEmpty();
        template.WorkingDir.Should().BeEmpty();
        template.Model.Should().BeNull("the type declares it nullable, so its own default is null");
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
