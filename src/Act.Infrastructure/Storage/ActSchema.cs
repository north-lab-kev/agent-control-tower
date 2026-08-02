using Act.Core.Model;
using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActSchema
{
    private const int BaselineVersion = 1;

    private const int DocumentId = 1;

    private static readonly Action<ILiteDatabase>[] Migrations = [];

    public static int CurrentVersion => BaselineVersion + Migrations.Length;

    public static void Apply(ILiteDatabase database)
    {
        Migrate(database);
        EnsureIndexes(database);
    }

    public static int StoredVersion(ILiteDatabase database)
        => database.GetCollection<SchemaDocument>(ActCollections.Schema).FindById(DocumentId)?.Version ?? 0;

    private static void Migrate(ILiteDatabase database)
    {
        var stored = StoredVersion(database);

        if (stored > CurrentVersion)
            throw new InvalidOperationException(
                $"The ACT store is at schema version {stored}, which is newer than this build understands " +
                $"({CurrentVersion}). Update ACT, or point ACT_DATA_DIR at a different store.");

        if (stored == CurrentVersion)
            return;

        for (var version = Math.Max(stored, BaselineVersion); version < CurrentVersion; version++)
            Migrations[version - BaselineVersion](database);

        database.GetCollection<SchemaDocument>(ActCollections.Schema)
            .Upsert(new SchemaDocument { Id = DocumentId, Version = CurrentVersion });
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
