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
