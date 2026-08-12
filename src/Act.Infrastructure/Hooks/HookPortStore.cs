using Act.Infrastructure.Storage;
using LiteDB;

namespace Act.Infrastructure.Hooks;

// Its own document rather than a field on `UserSettings`, because the port is machine state and
// not a preference: nothing in the settings page shows it, and `UserSettingsService` caches the
// settings object it loaded at startup — a port written into that object afterwards would be
// clobbered by the next preference change.
internal sealed class HookPortStore(ILiteDatabase database)
{
    private const int DocumentId = 1;

    public int Load() => Collection().FindById(DocumentId)?.Port ?? 0;

    public void Save(int port)
        => Collection().Upsert(new EndpointDocument { Id = DocumentId, Port = port });

    private ILiteCollection<EndpointDocument> Collection()
        => database.GetCollection<EndpointDocument>(ActCollections.Endpoint);

    private sealed class EndpointDocument
    {
        public int Id { get; set; }

        public int Port { get; set; }
    }
}
