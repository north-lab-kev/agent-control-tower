using Act.Core.Abstractions;
using Act.Core.Model;
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
    public void A_store_written_before_versioning_is_stamped_on_open()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        using (var legacy = new LiteDatabase(DatabasePath(temp.Path)))
            legacy.GetCollection("settings").Insert(new BsonDocument { ["_id"] = 1 });

        using (var provider = Provider(temp.Path))
            provider.GetRequiredService<ICardStore>();

        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    // The column merge renamed two stored enum names out of existence. Enums are stored by name, so
    // without the migration this is not a cosmetic problem — `GetAllAsync` throws on the first card
    // and the app cannot start.
    [Theory]
    [InlineData("NeedsFeedback", "NeedsPermission")]
    [InlineData("ToReview", "Idle")]
    public async Task A_card_stored_before_the_column_merge_is_rewritten(string column, string badge)
    {
        using var temp = new TempDirectory();
        var id = Guid.NewGuid();

        Directory.CreateDirectory(temp.Path);

        using (var legacy = new LiteDatabase(DatabasePath(temp.Path)))
        {
            legacy.GetCollection("cards").Insert(new BsonDocument
            {
                ["_id"] = id,
                ["Number"] = 1000,
                ["Title"] = "Written before the merge",
                ["Column"] = column,
                ["Badge"] = badge,
                ["Transitions"] = new BsonArray
                {
                    new BsonDocument { ["Column"] = "Executing", ["Badge"] = "Running" },
                    new BsonDocument { ["Column"] = column, ["Badge"] = badge },
                },
            });
        }

        Card? card;

        using (var provider = Provider(temp.Path))
            card = await provider.GetRequiredService<ICardStore>().GetAsync(id);

        card.Should().NotBeNull();
        card!.Column.Should().Be(BoardColumn.YourTurn);
        card.Transitions.Last().Column.Should().Be(BoardColumn.YourTurn);
        StoredVersion(temp.Path).Should().Be(ActSchema.CurrentVersion);
    }

    [Fact]
    public async Task The_badge_a_pre_merge_card_was_reviewable_with_survives_the_rename()
    {
        using var temp = new TempDirectory();
        var id = Guid.NewGuid();

        Directory.CreateDirectory(temp.Path);

        using (var legacy = new LiteDatabase(DatabasePath(temp.Path)))
        {
            legacy.GetCollection("cards").Insert(new BsonDocument
            {
                ["_id"] = id,
                ["Number"] = 1001,
                ["Title"] = "Awaiting sign-off",
                ["Column"] = "ToReview",
                ["Badge"] = "Idle",
            });
        }

        using var provider = Provider(temp.Path);

        var card = await provider.GetRequiredService<ICardStore>().GetAsync(id);

        card!.Badge.Should().Be(Badge.ReadyForReview);
        card.NeedsAttention.Should().BeTrue();
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
