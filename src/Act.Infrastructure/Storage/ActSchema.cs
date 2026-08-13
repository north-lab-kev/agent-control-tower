using Act.Core.Model;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Storage;

internal static class ActSchema
{
    private const int BaselineVersion = 1;

    private const int DocumentId = 1;

    private static readonly Action<ILiteDatabase, BsonMapper>[] Migrations =
    [
        (database, _) => ReplaceUpdatePolicyWithAutoUpdate(database),
    ];

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

    // Schema 2. The three-way `UpdatePolicy` became the `AutoUpdate` toggle, so the stored enum name
    // has to become a boolean before anything loads `UserSettings` — LiteDB persists enums by name and
    // its deserializer throws on a name the build no longer has, and this one loads in a constructor
    // at start-up, which takes the whole app down rather than one card.
    //
    // Only `NotifyAndDownload` becomes `true`: it is the one position that fetched a version on its
    // own. `NotifyOnly` said "tell me, download when I ask", so mapping it to `true` would start
    // downloading behind the back of the one user who explicitly refused that — they keep *Check now*
    // and the *Download* button, which is the same bargain by hand. A document with no `Updates` at
    // all predates the field and takes the model's default.
    //
    // Untyped `BsonDocument` access on purpose: the CLR shape no longer has the property being read,
    // so a mapped read here would throw on exactly the documents this exists to fix.
    private static void ReplaceUpdatePolicyWithAutoUpdate(ILiteDatabase database)
    {
        var collection = database.GetCollection(ActCollections.Settings);

        foreach (var document in collection.FindAll().ToList())
        {
            if (document["Settings"] is not BsonDocument settings)
                continue;

            var stored = settings["Updates"];

            settings.Remove("Updates");
            settings["AutoUpdate"] = !stored.IsString || stored.AsString == "NotifyAndDownload";

            collection.Update(document);
        }
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
