using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.Codex;

// ⚠️ PENDING — inference standing in for a hook that never fires. Codex writes one rollout file per
// session under `$CODEX_HOME/sessions/YYYY/MM/DD/rollout-<local-ts>-<uuid>.jsonl`, and its first line
// is a `session_meta` carrying `session_id` and `cwd`. That is enough to find the file ACT's own launch
// just created, and it is also **how a Codex card binds its session id at all** — there is no
// `--session-id` to pre-mint and no `SessionStart` payload to read it from.
//
// **Delete this class when Codex hooks start firing** and take the path and the id off the payload,
// exactly as `ClaudeCodeHookNormalizer` does. Everything below is convention-matching that a hook
// would make unnecessary: the folder layout, the filename shape, and the cwd/time/claimed triangulation
// that tells two launches in the same directory apart.
public sealed class CodexRolloutFinder(
    ITranscriptDirectory directory,
    ITranscriptReader reader) : ITranscriptFinder
{
    private const string Pattern = "rollout-*.jsonl";

    private const string SessionsFolder = "sessions";

    // Enough to cover several cards starting together, few enough that the head of each is cheap to
    // read. A rollout older than the last handful cannot belong to a launch ACT just made.
    private const int Candidates = 12;

    // The launch stamp and the file's own stamp are taken by different clocks. A second of slack
    // costs nothing — a stale rollout is still excluded by its cwd and by being already claimed.
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(2);

    public AgentType Agent => AgentType.Codex;

    public TranscriptLocation? Locate(TranscriptSearch search)
    {
        var root = Path.Combine(CodexHookConfig.ResolveCodexHome(), SessionsFolder);

        // Oldest first among the newest few: two cards launched seconds apart in one directory take
        // the files in the order they were created, so the first card does not steal the second's.
        var candidates = directory.Newest(root, Pattern, Candidates)
            .Where(path => !search.Claimed.Contains(path))
            .Reverse();

        foreach (var path in candidates)
        {
            if (Meta(path) is not { } meta)
                continue;

            if (!SameDirectory(meta.Cwd, search.WorkingDir))
                continue;

            if (meta.StartedAt is { } startedAt && startedAt + Skew < search.LaunchedAt)
                continue;

            return new TranscriptLocation(path, meta.SessionId);
        }

        return null;
    }

    // The first line only. A rollout that has just been created is a few hundred bytes, and by the
    // time it is not, it has long since been claimed.
    private RolloutMeta? Meta(string path)
    {
        var head = reader.Read(path, 0).Lines.FirstOrDefault();

        if (head is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(head);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object || Text(root, "type") != "session_meta")
                return null;

            if (!root.TryGetProperty("payload", out var payload)
                || payload.ValueKind is not JsonValueKind.Object)
                return null;

            return new RolloutMeta(
                Text(payload, "session_id") ?? Text(payload, "id"),
                Text(payload, "cwd"),
                Moment(root, payload));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The outer timestamp is when the line was written and the payload's is when the session began;
    // either identifies the launch, and the outer one is the one always present.
    private static DateTimeOffset? Moment(JsonElement root, JsonElement payload)
        => Stamp(root, "timestamp") ?? Stamp(payload, "timestamp");

    private static DateTimeOffset? Stamp(JsonElement element, string name)
        => Text(element, name) is { } text
            && DateTimeOffset.TryParse(
                text,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var stamp)
            ? stamp
            : null;

    // Codex reports the cwd as it resolved it, which is not always spelled the way ACT stored it —
    // separators and a trailing slash differ without meaning anything.
    private static bool SameDirectory(string? left, string? right)
    {
        if (left is null || right is null)
            return false;

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
        => path.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record RolloutMeta(string? SessionId, string? Cwd, DateTimeOffset? StartedAt);
}
