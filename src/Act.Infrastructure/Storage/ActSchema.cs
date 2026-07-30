using Act.Core.Model;
using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActSchema
{
    private const int BaselineVersion = 1;

    private const int DocumentId = 1;

    private static readonly Action<ILiteDatabase>[] Migrations = [MergeYourTurn];

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

    // Needs feedback and To review became the one Your turn column, and the `Idle` badge became
    // `ReadyForReview`. Enums are stored *by name* — deliberately, so reordering them cannot shift
    // stored values — which means every card already in the store names a column this build cannot
    // parse, and an unparsed enum is a startup crash rather than one odd-looking card.
    //
    // Done in raw BSON on purpose: the mapper is the thing that fails, so nothing here may go through
    // it. Transitions carry the same two fields and are rewritten with the same rules, because a
    // card's history is read back as strongly typed as the card is.
    private static void MergeYourTurn(ILiteDatabase database)
    {
        var cards = database.GetCollection(ActCollections.Cards);

        foreach (var card in cards.FindAll().ToList())
        {
            var rewritten = Rename(card);

            if (card["Transitions"].IsArray)
            {
                foreach (var transition in card["Transitions"].AsArray.OfType<BsonDocument>())
                    rewritten |= Rename(transition);
            }

            if (rewritten)
                cards.Update(card);
        }
    }

    private static bool Rename(BsonDocument document)
    {
        var rewritten = false;

        if (RenamedColumn(document["Column"]) is { } column)
        {
            document["Column"] = column;
            rewritten = true;
        }

        if (RenamedBadge(document["Badge"]) is { } badge)
        {
            document["Badge"] = badge;
            rewritten = true;
        }

        return rewritten;
    }

    private static string? RenamedColumn(BsonValue value) => value.IsString
        ? value.AsString switch
        {
            "NeedsFeedback" or "ToReview" => nameof(BoardColumn.YourTurn),
            _ => null,
        }
        : null;

    private static string? RenamedBadge(BsonValue value)
        => value.IsString && value.AsString is "Idle" ? nameof(Badge.ReadyForReview) : null;

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
