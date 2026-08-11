using System.Text;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Infrastructure.FileSystem;

// Where a task's attached files live: `<dataDir>/attachments/<cardId>/`. ACT's own directory and
// never the task's working directory — ACT writes nothing into a user's repository, and an
// attachment dropped into one is one `autoGit` commit away from being pushed.
//
// A copy, not a reference. A paste has no source path at all, so referencing in place could not
// cover all three input routes; and a referenced file the user later moves or deletes would leave
// a card that cannot be re-run.
//
// The port is declared here beside its implementation rather than in `Act.Core/Abstractions`,
// because nothing in the core triggers it — the same line that keeps `INotifier` in `Act.App`
// and puts `IWorkingDirectories` in the core, which `WorkingDirConflict` really does call.
public interface IAttachmentStore
{
    // Created, not just named: both the Claude `--add-dir` grant and a mid-session drop need the
    // directory to exist before there is anything in it.
    string DirectoryFor(Guid cardId);

    Task<TaskAttachment> SaveAsync(
        Guid cardId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    string PathFor(Guid cardId, TaskAttachment attachment);

    // The path of a file this card really holds, or null. Unlike `PathFor` it trusts nothing: the name
    // arrives off a url, so it is resolved and then *proved* to sit inside the card's own folder. The
    // check lives here rather than in the route for the same reason `HookPortGuard` is a rule in this
    // project rather than a middleware — a boundary belongs somewhere a unit test can attack it.
    string? ResolveInside(Guid cardId, string fileName);

    // Bring the directory back to exactly one list of names. It is what makes add-then-discard and
    // remove-then-save both end honest: files are written the moment they are attached, so the only
    // moment the disk and the card are guaranteed to agree is when the form commits one way or the
    // other, and that is when this runs.
    void Prune(Guid cardId, IEnumerable<string> keep);

    void Copy(Guid fromCardId, Guid toCardId);

    void Clear(Guid cardId);

    // Every card id the directory holds files for, so the startup sweep can spot the ones no card
    // claims — a task abandoned before its first save.
    IReadOnlyList<Guid> CardIds();
}

public sealed class AttachmentStore(string dataDirectory, IClock clock) : IAttachmentStore
{
    public const string DirectoryName = "attachments";

    // Long enough for any name a person types and short enough that the whole path stays inside
    // the platform limit once the data directory and the card's guid are in front of it.
    private const int MaxBaseNameLength = 80;

    // What Chromium calls a bitmap pasted from the clipboard — it has no name of its own, so the
    // browser invents this one. Left alone, a second screenshot becomes `image (2).png` and a card
    // ends up with a numbered set of files nothing distinguishes; a timestamp at least says when.
    private const string PastedPlaceholder = "image";

    // The union of what Windows, macOS and Linux refuse, so the same name lands the same way
    // everywhere and a card written on one box opens on another.
    private const string Forbidden = "<>:\"/\\|?*";

    private string Root => Path.Combine(dataDirectory, DirectoryName);

    public string DirectoryFor(Guid cardId)
    {
        var directory = Path.Combine(Root, cardId.ToString("d"));

        Directory.CreateDirectory(directory);

        return directory;
    }

    public async Task<TaskAttachment> SaveAsync(
        Guid cardId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var directory = DirectoryFor(cardId);
        var name = Available(directory, Sanitized(fileName));
        var path = Path.Combine(directory, name);

        // `CreateNew` rather than `Create`: `Available` just proved the name is free, and a race
        // that lost should fail loudly rather than overwrite a file the user can no longer see.
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new TaskAttachment
        {
            FileName = name,
            Length = new FileInfo(path).Length,
            AddedAt = clock.Now,
        };
    }

    public string PathFor(Guid cardId, TaskAttachment attachment)
        => Path.Combine(Root, cardId.ToString("d"), attachment.FileName);

    // Three escapes to stop, and `GetFullPath` on both sides is what stops all three: `..\..\evil`
    // normalises out of the folder, an absolute second argument makes `Path.Combine` discard the first
    // entirely, and a trailing separator on the prefix test is what keeps `…\<id>` from matching
    // `…\<id>-evil`. Existence is checked last so a probe cannot tell a blocked path from a missing
    // one — both are simply null.
    public string? ResolveInside(Guid cardId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string directory;
        string candidate;

        try
        {
            directory = Path.GetFullPath(Path.Combine(Root, cardId.ToString("d")));
            candidate = Path.GetFullPath(Path.Combine(directory, fileName));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }

        var inside = candidate.StartsWith(
            directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

        return inside && File.Exists(candidate) ? candidate : null;
    }

    public void Prune(Guid cardId, IEnumerable<string> keep)
    {
        var directory = Path.Combine(Root, cardId.ToString("d"));

        if (!Directory.Exists(directory))
            return;

        var kept = keep.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (kept.Contains(Path.GetFileName(file)))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Copy(Guid fromCardId, Guid toCardId)
    {
        var source = Path.Combine(Root, fromCardId.ToString("d"));

        if (!Directory.Exists(source))
            return;

        var target = DirectoryFor(toCardId);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
    }

    public void Clear(Guid cardId)
    {
        var directory = Path.Combine(Root, cardId.ToString("d"));

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<Guid> CardIds()
    {
        if (!Directory.Exists(Root))
            return [];

        List<Guid> ids = [];

        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            if (Guid.TryParse(Path.GetFileName(directory), out var id))
                ids.Add(id);
        }

        return ids;
    }

    // A browser hands over whatever the source called the file, which on a drop is a real name and on
    // a paste is the placeholder above. Neither is trusted with a path.
    //
    // Both separators and one fixed forbidden set, whichever OS is running — the same stance
    // `WorkingDirectory.Resolve` takes and for the same reason. `Path.GetFileName` and
    // `Path.GetInvalidFileNameChars` answer for the platform ACT happens to be on, so on Linux they
    // would leave `..\..\evil.txt` whole and keep a `?` a Windows share cannot store.
    private string Sanitized(string? fileName)
    {
        var given = fileName?.Trim() ?? string.Empty;
        var cut = given.LastIndexOfAny(['/', '\\']);
        var name = cut < 0 ? given : given[(cut + 1)..];

        var cleaned = new StringBuilder(name.Length);

        foreach (var character in name)
            cleaned.Append(Forbidden.Contains(character) || char.IsControl(character) ? '-' : character);

        name = cleaned.ToString();

        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name).Trim(' ', '.');

        // Windows stores neither a trailing dot nor a bare `.`, and both are what a name made only of
        // dots leaves behind once the trim above has run.
        if (extension is ".")
            extension = string.Empty;

        if (baseName.Length == 0 || baseName == PastedPlaceholder)
            baseName = $"pasted-{clock.Now.ToLocalTime():yyyyMMdd-HHmmss}";

        if (baseName.Length > MaxBaseNameLength)
            baseName = baseName[..MaxBaseNameLength];

        return baseName + extension;
    }

    // `name (2).ext`, then `(3)`, and so on. Attaching the same file twice is a normal accident and
    // silently overwriting the first would lose whichever copy the user actually wanted.
    private static string Available(string directory, string name)
    {
        if (!File.Exists(Path.Combine(directory, name)))
            return name;

        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);

        for (var attempt = 2; ; attempt++)
        {
            var candidate = $"{baseName} ({attempt}){extension}";

            if (!File.Exists(Path.Combine(directory, candidate)))
                return candidate;
        }
    }
}
