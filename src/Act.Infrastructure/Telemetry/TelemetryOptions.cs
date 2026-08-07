using System.Text;

namespace Act.Infrastructure.Telemetry;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    // What a project key looks like, and the whole reason both encodings can be told apart without a
    // flag: `_` is not in the base64 alphabet, so a plain key can never be mistaken for an encoded one.
    private const string KeyPrefix = "phc_";

    private const string DefaultHost = "https://us.i.posthog.com";

    private const int ShortestFlushSeconds = 5;

    private const int LongestFlushSeconds = 600;

    private const int SmallestFlushAt = 1;

    private const int LargestFlushAt = 100;

    public bool Enabled { get; init; } = true;

    // A PostHog *project* key, plain (`phc_…`) or base64 of the same. The release pipeline stamps the
    // encoded form so the installer does not carry the key in clear; a local run can paste the plain
    // one and nothing has to be configured to say which.
    //
    // **This is obfuscation, not protection, and must not be treated as a place to keep a secret.**
    // A project key is write-only by design — PostHog publishes it in the script tag of every site
    // that uses them — so there is nothing here worth defending, and base64 would not defend it. A
    // personal API key must never appear here in either encoding. See *The token is encoded, and that
    // is not security* in `docs/design-notes.md`.
    public string? ProjectToken { get; init; }

    // The key as the client needs it, whichever way it was written down.
    public string? Token => Resolved(ProjectToken);

    public string Host { get; init; } = DefaultHost;

    public int FlushSeconds { get; init; } = 30;

    public int FlushAt { get; init; } = 20;

    // Both halves have to be true before anything is wired: the switch in configuration, and a key
    // that actually resolves. A build with no key — or with something in that slot that is neither a
    // project key nor base64 of one — gets `NullTelemetrySink`, so tests and a fresh clone are silent
    // without anyone having to remember to turn something off.
    public bool Configured => Enabled && Token is not null && HostUri is not null;

    public Uri? HostUri
        => Uri.TryCreate(Host, UriKind.Absolute, out var host)
            && host.Scheme is "https" or "http"
                ? host
                : null;

    public TimeSpan FlushInterval
        => TimeSpan.FromSeconds(Math.Clamp(FlushSeconds, ShortestFlushSeconds, LongestFlushSeconds));

    public int FlushBatch => Math.Clamp(FlushAt, SmallestFlushAt, LargestFlushAt);

    // Both forms have to end at a real project key, and anything that does not is treated as
    // unconfigured rather than passed on. Sending a malformed key would fail every batch silently at
    // the far end, which is a worse answer than never having started a client.
    private static string? Resolved(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        var written = configured.Trim();

        if (written.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return written;

        return Decoded(written) is { } decoded && decoded.StartsWith(KeyPrefix, StringComparison.Ordinal)
            ? decoded
            : null;
    }

    private static string? Decoded(string written)
    {
        var buffer = new byte[written.Length];

        return Convert.TryFromBase64String(written, buffer, out var length)
            ? Encoding.UTF8.GetString(buffer, 0, length)
            : null;
    }
}
