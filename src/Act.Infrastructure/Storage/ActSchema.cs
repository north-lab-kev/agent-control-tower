using Act.Core.Model;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Storage;

internal static class ActSchema
{
    private const int BaselineVersion = 1;

    private const int DocumentId = 1;

    private static readonly Action<ILiteDatabase, BsonMapper>[] Migrations =
        [TaskDefaultsBecomeTemplates, NullFieldsBecomeDefaults];

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

    // Schema 1 → 2. `UserSettings.TaskDefaults` — one unnamed set of new-task defaults — became
    // `UserSettings.Templates`, a list the user manages, so what the single stored set described is
    // now the default template. Written through the mapper rather than by hand: the shape it has to
    // produce is whatever `TaskTemplate` serializes to, and the two field names LiteDB does not take
    // verbatim (`Id` → `_id`, an enum's encoding) are exactly the ones a hand-built document gets
    // wrong. `TaskDefaults`'s fields are a subset of `TaskTemplate`'s under the same names, which is
    // what lets the old document deserialize straight into the new type.
    private static void TaskDefaultsBecomeTemplates(ILiteDatabase database, BsonMapper mapper)
    {
        var settings = database.GetCollection(ActCollections.Settings);

        foreach (var document in settings.FindAll().ToList())
        {
            if (document["Settings"] is not { IsDocument: true } value)
                continue;

            var stored = value.AsDocument;

            var template = stored["TaskDefaults"] is { IsDocument: true } defaults
                ? mapper.ToObject<TaskTemplate>(defaults.AsDocument)
                : new TaskTemplate();

            template.Id = Guid.NewGuid();
            template.IsDefault = true;

            stored.Remove("TaskDefaults");
            stored["Templates"] = new BsonArray { mapper.ToDocument(template) };

            settings.Update(document);
        }
    }

    // Schema 2 → 3. Removing a null field rather than rewriting it is load-bearing, and not
    // interchangeable with writing "" — see *Empty strings were stored as null* in
    // `docs/design-notes.md` before changing either half.
    private static void NullFieldsBecomeDefaults(ILiteDatabase database, BsonMapper mapper)
    {
        foreach (var name in database.GetCollectionNames().ToList())
        {
            var collection = database.GetCollection(name);

            foreach (var document in collection.FindAll().ToList())
            {
                if (DropNulls(document))
                    collection.Update(document);
            }
        }
    }

    private static bool DropNulls(BsonDocument document)
    {
        var dropped = false;

        foreach (var key in document.Keys.ToList())
        {
            if (document[key].IsNull)
            {
                document.Remove(key);
                dropped = true;
            }
            else
                dropped |= DropNullsWithin(document[key]);
        }

        return dropped;
    }

    private static bool DropNullsWithin(BsonValue value)
    {
        if (value.IsDocument)
            return DropNulls(value.AsDocument);

        if (!value.IsArray)
            return false;

        var dropped = false;

        foreach (var item in value.AsArray)
            dropped |= DropNullsWithin(item);

        return dropped;
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
