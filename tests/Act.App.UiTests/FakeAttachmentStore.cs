using Act.Core.Model;
using Act.Infrastructure.FileSystem;

namespace Act.App.UiTests;

// In memory, so a board test never touches a real directory — the same reason `FakeCardStore` exists
// beside it. What it records is which calls the board and the launcher actually make.
internal sealed class FakeAttachmentStore : IAttachmentStore
{
    private readonly Dictionary<Guid, List<string>> files = [];

    public List<Guid> Cleared { get; } = [];

    public List<(Guid From, Guid To)> Copied { get; } = [];

    public List<(Guid Card, string[] Keep)> Pruned { get; } = [];

    public string DirectoryFor(Guid cardId) => $"/attachments/{cardId:d}";

    public Task<TaskAttachment> SaveAsync(
        Guid cardId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var name = fileName ?? "attachment";

        if (!files.TryGetValue(cardId, out var held))
            files[cardId] = held = [];

        held.Add(name);

        return Task.FromResult(new TaskAttachment { FileName = name });
    }

    public string PathFor(Guid cardId, TaskAttachment attachment)
        => $"{DirectoryFor(cardId)}/{attachment.FileName}";

    // The real containment rule is a filesystem property and is attacked in `AttachmentStoreTests`;
    // here it only has to answer for names this fake was actually given.
    public string? ResolveInside(Guid cardId, string fileName)
        => files.TryGetValue(cardId, out var held) && held.Contains(fileName)
            ? $"{DirectoryFor(cardId)}/{fileName}"
            : null;

    public void Prune(Guid cardId, IEnumerable<string> keep) => Pruned.Add((cardId, [.. keep]));

    public void Copy(Guid fromCardId, Guid toCardId) => Copied.Add((fromCardId, toCardId));

    public void Clear(Guid cardId)
    {
        Cleared.Add(cardId);

        files.Remove(cardId);
    }

    public IReadOnlyList<Guid> CardIds() => [.. files.Keys];
}
