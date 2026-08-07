using Act.Core.Model;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Storage;

internal static class ActSchema
{
    private const int BaselineVersion = 1;

    private const int DocumentId = 1;

    private static readonly Action<ILiteDatabase, BsonMapper>[] Migrations = [];

    public static int CurrentVersion => BaselineVersion + Migrations.Length;

    public static void Apply(ILiteDatabase database, BsonMapper mapper, ILogger log)
    {
        Migrate(database, mapper, log);
        EnsureIndexes(database);
    }

    public static int StoredVersion(ILiteDatabase database)
        => database.GetCollection<SchemaDocument>(ActCollections.Schema).FindById(DocumentId)?.Version ?? 0;

    private static void Migrate(ILiteDatabase database, BsonMapper mapper, ILogger log)
    {
        var stored = StoredVersion(database);

        if (stored > CurrentVersion)
        {
            log.LogCritical(
                "The store is at schema {Stored}, newer than this build understands ({Current}).",
                stored,
                CurrentVersion);

            throw new InvalidOperationException(
                $"The ACT store is at schema version {stored}, which is newer than this build understands " +
                $"({CurrentVersion}). Update ACT, or point ACT_DATA_DIR at a different store.");
        }

        if (stored == CurrentVersion)
        {
            log.LogDebug("The store is at schema {Version}.", stored);

            return;
        }

        if (stored == 0)
            log.LogInformation("A new store; applying the schema up to {Current}.", CurrentVersion);
        else
            log.LogInformation("Migrating the store from schema {Stored} to {Current}.", stored, CurrentVersion);

        for (var version = Math.Max(stored, BaselineVersion); version < CurrentVersion; version++)
        {
            log.LogInformation("Applying schema {From} → {To}.", version, version + 1);

            Migrations[version - BaselineVersion](database, mapper);
        }

        database.GetCollection<SchemaDocument>(ActCollections.Schema)
            .Upsert(new SchemaDocument { Id = DocumentId, Version = CurrentVersion });

        log.LogInformation("The store is now at schema {Version}.", CurrentVersion);
    }

    private static void EnsureIndexes(ILiteDatabase database)
    {
        var cards = database.GetCollection<Card>(ActCollections.Cards);

        cards.EnsureIndex(card => card.Number, unique: true);
        cards.EnsureIndex(card => card.Column);
        cards.EnsureIndex(card => card.SessionId);
    }

    internal sealed class SchemaDocument
    {
        public int Id { get; set; }

        public int Version { get; set; }
    }
}
