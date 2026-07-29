using Act.Core.Abstractions;
using Act.Core.Model;
using LiteDB;

namespace Act.Infrastructure.Storage;

internal sealed class LiteDbSettingsStore(ILiteDatabase database) : ISettingsStore
{
    private const int DocumentId = 1;

    public UserSettings Load()
        => Collection().FindById(DocumentId)?.Settings ?? new UserSettings();

    public void Save(UserSettings settings)
        => Collection().Upsert(new SettingsDocument { Id = DocumentId, Settings = settings });

    private ILiteCollection<SettingsDocument> Collection()
        => database.GetCollection<SettingsDocument>(ActCollections.Settings);

    private sealed class SettingsDocument
    {
        public int Id { get; set; }

        public UserSettings Settings { get; set; } = new();
    }
}
