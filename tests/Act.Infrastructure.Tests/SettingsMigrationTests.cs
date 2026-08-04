using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Tests;

// Schema 1 → 2: `UserSettings.TaskDefaults` became `UserSettings.Templates`, and what the single
// stored set of defaults described is now the default template. A migration rather than a read-time
// shim, because nothing ever tells you the last old document is gone.
public class SettingsMigrationTests
{
    [Fact]
    public void The_stored_task_defaults_become_the_default_template()
    {
        using var temp = new TempDirectory();

        WriteSchemaOneSettings(temp.Path, new BsonDocument
        {
            ["Language"] = "French",
            ["TaskDefaults"] = new BsonDocument
            {
                ["WorkingDir"] = "C:/dev/act",
                ["Agent"] = "Codex",
                ["Model"] = "gpt-5.6-terra",
                ["Effort"] = "high",
                ["PermissionMode"] = "AcceptEdits",
                ["Schedule"] = "NextWindow",
                ["GitAction"] = "PullRequest",
                ["Draft"] = true,
            },
        });

        var settings = Migrated(temp.Path);

        var template = settings.Templates.Should().ContainSingle().Subject;

        template.IsDefault.Should().BeTrue();
        template.Id.Should().NotBeEmpty();
        template.WorkingDir.Should().Be("C:/dev/act");
        template.Agent.Should().Be(AgentType.Codex);
        template.Model.Should().Be("gpt-5.6-terra");
        template.Effort.Should().Be("high");
        template.PermissionMode.Should().Be(PermissionMode.AcceptEdits);
        template.Schedule.Should().Be(TaskSchedule.NextWindow);
        template.GitAction.Should().Be(GitAction.PullRequest);
        template.Draft.Should().BeTrue();

        // The whole point of a migration over a shim: after it runs there is exactly one shape on
        // disk, so nothing downstream has to know the old field ever existed.
        var document = StoredSettings(temp.Path);

        document.ContainsKey("TaskDefaults").Should().BeFalse();

        // And the shape it writes is the *current* one, not merely the fields the old document
        // happened to carry — which is what writing through the mapper buys. `Title` has no
        // counterpart in `TaskDefaults`, so it is the field that proves it.
        document["Templates"].AsArray[0].AsDocument.ContainsKey("Title").Should().BeTrue();

        settings.Language.Should().Be(LanguagePreference.French, "the rest of the document is untouched");
    }

    // A store written before the defaults were ever edited has no sub-document to lift, and must still
    // come out with the one default template every reader assumes.
    [Fact]
    public void A_document_with_no_stored_defaults_still_gets_a_default_template()
    {
        using var temp = new TempDirectory();

        WriteSchemaOneSettings(temp.Path, new BsonDocument { ["Density"] = "Compact" });

        var settings = Migrated(temp.Path);

        settings.Templates.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
        settings.Density.Should().Be(BoardDensity.Compact);
    }

    // Schema 2 → 3. What made this necessary is that LiteDB's mapper wrote every empty string as
    // null, so a property the type declares non-nullable came back null and the first line to
    // dereference one threw — on the second start of any store, not just an old one.
    [Fact]
    public void An_empty_string_survives_a_round_trip_through_the_store()
    {
        using var temp = new TempDirectory();

        using (var provider = Provider(temp.Path))
        {
            var store = provider.GetRequiredService<ISettingsStore>();
            var settings = new UserSettings();

            settings.Templates.Add(new TaskTemplate { Id = Guid.NewGuid(), IsDefault = true });

            store.Save(settings);
        }

        using (var provider = Provider(temp.Path))
        {
            var template = provider.GetRequiredService<ISettingsStore>().Load()
                .Templates.Should().ContainSingle().Subject;

            template.Name.Should().BeEmpty();
            template.Title.Should().BeEmpty();
            template.Prompt.Should().BeEmpty();
            template.WorkingDir.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_stored_null_is_read_back_as_the_types_own_default()
    {
        using var temp = new TempDirectory();

        WriteSettings(temp.Path, version: 2, new BsonDocument
        {
            ["Templates"] = new BsonArray
            {
                new BsonDocument
                {
                    ["_id"] = Guid.NewGuid(),
                    ["IsDefault"] = true,
                    ["Name"] = BsonValue.Null,
                    ["Title"] = BsonValue.Null,
                    ["Model"] = BsonValue.Null,
                },
            },
        });

        var template = Migrated(temp.Path).Templates.Should().ContainSingle().Subject;

        template.Name.Should().BeEmpty();
        template.Title.Should().BeEmpty();
        template.Model.Should().BeNull("the type declares it nullable, so its own default is null");

        StoredSettings(temp.Path)["Templates"].AsArray[0].AsDocument
            .ContainsKey("Name").Should().BeFalse("the migration removes the field, it does not rewrite it");
    }

    private static UserSettings Migrated(string dataDirectory)
    {
        using var provider = Provider(dataDirectory);

        return provider.GetRequiredService<ISettingsStore>().Load();
    }

    private static void WriteSchemaOneSettings(string dataDirectory, BsonDocument settings)
        => WriteSettings(dataDirectory, version: 1, settings);

    private static void WriteSettings(string dataDirectory, int version, BsonDocument settings)
    {
        Directory.CreateDirectory(dataDirectory);

        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        database.GetCollection("schema").Upsert(new BsonDocument { ["_id"] = 1, ["Version"] = version });

        database.GetCollection("settings").Upsert(new BsonDocument
        {
            ["_id"] = 1,
            ["Settings"] = settings,
        });
    }

    private static BsonDocument StoredSettings(string dataDirectory)
    {
        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        return database.GetCollection("settings").FindById(1)["Settings"].AsDocument;
    }

    private static string DatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "act.db");

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}
