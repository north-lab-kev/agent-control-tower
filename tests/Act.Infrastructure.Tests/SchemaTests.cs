using Act.Core.Abstractions;
using Act.Infrastructure.Storage;
using AwesomeAssertions;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Tests;

public class SchemaTests
{
    [Fact]
    public void A_new_store_is_stamped_with_the_current_version()
    {
        using var temp = new TempDirectory();

        using (var provider = Provider(temp.Path))
            provider.GetRequiredService<ICardStore>();

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    [Fact]
    public void A_store_already_at_the_current_version_is_opened_unchanged()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion);

        using (var provider = Provider(temp.Path))
        {
            var open = () => provider.GetRequiredService<ICardStore>();

            open.Should().NotThrow();
        }

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    // The adopt path: a store written before versioning holds data but no schema document, and it
    // flows through the whole migration loop — from the baseline up — before the stamp. What it held
    // has to survive the trip; the loop's bounds are exactly the code a regression here would ship
    // green, since the other cases cover only a fresh store and a rejection.
    [Fact]
    public async Task A_store_written_before_versioning_is_adopted_stamped_and_keeps_its_data()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        using (var database = new LiteDatabase(DatabasePath(temp.Path)))
        {
            database.GetCollection("cards").Insert(new BsonDocument
            {
                ["_id"] = Guid.NewGuid(),
                ["Number"] = 1039,
                ["Title"] = "Written before versioning",
            });
        }

        await using (var provider = Provider(temp.Path))
        {
            var store = provider.GetRequiredService<ICardStore>();
            var cards = await store.GetAllAsync();

            cards.Should().ContainSingle().Which.Title.Should().Be("Written before versioning");
        }

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    // Schema 2. The three-way `UpdatePolicy` became the `AutoUpdate` toggle, and the stored enum *name*
    // has to be gone before anything loads `UserSettings`: LiteDB persists enums by name and throws on
    // one the build no longer has, and settings load in a constructor at start-up — so getting this
    // wrong is not a lost preference, it is an app that will not open.
    //
    // Only `NotifyAndDownload` becomes true. `NotifyOnly` refused unasked downloads, so mapping it to
    // true would start fetching behind the back of the one user who said not to.
    [Theory]
    [InlineData("NotifyAndDownload", true)]
    [InlineData("NotifyOnly", false)]
    [InlineData("Off", false)]
    public void The_retired_update_policy_becomes_the_toggle(string stored, bool expected)
    {
        using var temp = new TempDirectory();

        WriteSettings(temp.Path, settings => settings["Updates"] = stored);

        using (var provider = Provider(temp.Path))
            provider.GetRequiredService<ISettingsStore>().Load().AutoUpdate.Should().Be(expected);

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);

        // The old field is gone from disk, not merely ignored on the way in — a document that still
        // holds it is a document the next reader can disagree about.
        ReadSettings(temp.Path).ContainsKey("Updates").Should().BeFalse();
    }

    // A settings document written before the field existed at all. It has to take the model's default
    // rather than the `false` that a missing-means-nothing reading would produce.
    [Fact]
    public void A_settings_document_with_no_policy_at_all_keeps_checking()
    {
        using var temp = new TempDirectory();

        WriteSettings(temp.Path, settings => settings["Theme"] = "Dark");

        using var provider = Provider(temp.Path);

        provider.GetRequiredService<ISettingsStore>().Load().AutoUpdate.Should().BeTrue();
    }

    // Nothing has ever been saved, so there is no document to rewrite. The migration must not invent
    // one, and must not throw on its absence.
    [Fact]
    public void A_store_with_no_settings_document_migrates_without_inventing_one()
    {
        using var temp = new TempDirectory();

        Stamp(temp.Path, 1);

        using (var provider = Provider(temp.Path))
            provider.GetRequiredService<ISettingsStore>().Load().AutoUpdate.Should().BeTrue();

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    private static void WriteSettings(string dataDirectory, Action<BsonDocument> build)
    {
        Stamp(dataDirectory, 1);

        var settings = new BsonDocument();

        build(settings);

        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        database.GetCollection("settings").Upsert(new BsonDocument
        {
            ["_id"] = 1,
            ["Settings"] = settings,
        });
    }

    private static BsonDocument ReadSettings(string dataDirectory)
    {
        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        return database.GetCollection("settings").FindById(1)["Settings"].AsDocument;
    }

    // Asked before the container opens anything, because opening it is what throws — and that throw
    // lands on the startup thread with no window and no Electron bridge, so a downgraded ACT would die
    // behind its own splash screen. The probe is what lets the shell say why instead.
    [Fact]
    public void A_store_from_a_newer_build_is_refused_before_it_is_opened()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion + 1);

        var store = ActStoreCompatibility.Inspect(temp.Path);

        store.IsSupported.Should().BeFalse();
        store.Stored.Should().Be(ActSchema.CurrentVersion + 1);
        store.Understood.Should().Be(ActSchema.CurrentVersion);
    }

    [Fact]
    public void A_store_this_build_wrote_is_accepted()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion);

        ActStoreCompatibility.Inspect(temp.Path).IsSupported.Should().BeTrue();
    }

    // Both migrate forward, so both are supported — only a *higher* number is refused. A store written
    // before versioning reports 0 and has no schema document at all.
    [Fact]
    public void A_store_written_before_versioning_is_accepted()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        using (var database = new LiteDatabase(DatabasePath(temp.Path)))
            database.GetCollection("cards").Insert(new BsonDocument { ["_id"] = Guid.NewGuid() });

        var store = ActStoreCompatibility.Inspect(temp.Path);

        store.IsSupported.Should().BeTrue();
        store.Stored.Should().Be(0);
    }

    // `new LiteDatabase` creates the file, so a probe that opened unconditionally would leave an empty
    // `act.db` behind on every first run — and hand the real open a store to migrate from nothing.
    [Fact]
    public void Probing_a_store_that_does_not_exist_yet_creates_nothing()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        ActStoreCompatibility.Inspect(temp.Path).IsSupported.Should().BeTrue();

        File.Exists(DatabasePath(temp.Path)).Should().BeFalse();
    }

    // The probe has to leave the file openable: the real open happens moments later, in the container.
    [Fact]
    public void Probing_does_not_leave_the_store_locked()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion);

        ActStoreCompatibility.Inspect(temp.Path);

        using var provider = Provider(temp.Path);

        var open = () => provider.GetRequiredService<ICardStore>();

        open.Should().NotThrow();
    }

    [Fact]
    public void A_store_from_a_newer_build_is_rejected()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion + 1);

        using var provider = Provider(temp.Path);

        var open = () => provider.GetRequiredService<ICardStore>();

        open.Should().Throw<InvalidOperationException>()
            .WithMessage("*newer than this build*");
    }

    [Fact]
    public void A_rejected_store_is_not_left_locked()
    {
        using var temp = new TempDirectory();
        Stamp(temp.Path, ActSchema.CurrentVersion + 1);

        using (var provider = Provider(temp.Path))
        {
            var open = () => provider.GetRequiredService<ICardStore>();

            open.Should().Throw<InvalidOperationException>();
        }

        var reopen = () => new LiteDatabase(DatabasePath(temp.Path)).Dispose();

        reopen.Should().NotThrow();
    }

    private static void Stamp(string dataDirectory, int version)
    {
        Directory.CreateDirectory(dataDirectory);

        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        database.GetCollection("schema").Upsert(new BsonDocument
        {
            ["_id"] = 1,
            ["Version"] = version,
        });
    }

    private static int StoredVersion(string dataDirectory)
    {
        using var database = new LiteDatabase(DatabasePath(dataDirectory));

        return database.GetCollection("schema").FindById(1)["Version"].AsInt32;
    }

    private static string DatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "act.db");

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}
