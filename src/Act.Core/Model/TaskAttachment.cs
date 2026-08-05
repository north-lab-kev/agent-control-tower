namespace Act.Core.Model;

// One file handed to a task alongside its prompt. Only the *name* is stored, never a path: the
// file lives in ACT's own data directory under the card's id, and that root moves with
// `ACT_DATA_DIR` — an absolute path frozen on a card would rot the same way a per-card
// `agentBinary` would (see `LaunchComposition`).
public sealed class TaskAttachment
{
    // Generous for the things people actually attach — a screenshot, a stack trace, a spec — and
    // bounded because the store grows without anything sweeping it: a card keeps its files until the
    // archive is purged. `MaxLength` is also the ceiling the browser upload has to be told, since
    // `IBrowserFile.OpenReadStream` refuses to guess one.
    public const int MaxPerTask = 10;

    public const long MaxLength = 25L * 1024 * 1024;

    // One list, two callers: `codex --image` accepts exactly these, and they are also the only things
    // the preview route will serve. Keeping the media type here rather than in the route is what
    // stops the two lists drifting — an extension this does not name is not an image to either of
    // them.
    //
    // **SVG is deliberately absent.** It is an image everywhere else and a script host here: served
    // from ACT's own origin it could run against the app's own page. Nothing about an attachment
    // needs it, so it stays a file the agent reads by path like any other.
    public static string? ImageContentTypeFor(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null,
        };

    public string FileName { get; set; } = string.Empty;

    public long Length { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public string? ImageContentType => ImageContentTypeFor(FileName);

    public bool IsImage => ImageContentType is not null;
}
