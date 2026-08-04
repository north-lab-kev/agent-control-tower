using System.Globalization;
using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

public sealed class ClaudeCodeUsageDialect : IUsageDialect
{
    private const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    private const string ConfigFolder = ".claude";

    private const string FileName = ".credentials.json";

    public AgentType Agent => AgentType.ClaudeCode;

    public string DefaultEndpoint => "https://api.anthropic.com/api/oauth/usage";

    public string DefaultCredentialsPath()
    {
        var configured = Environment.GetEnvironmentVariable(ConfigDirVariable);

        var home = configured is { Length: > 0 }
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigFolder);

        return Path.Combine(home, FileName);
    }

    public UsageToken Token(string credentials, DateTimeOffset now)
    {
        if (Document(credentials) is not { } root)
            return UsageToken.Missing;

        if (!root.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind is not JsonValueKind.Object)
            return UsageToken.Missing;

        // Two clocks, and the file states both: the access token lasts 8 hours, the refresh token
        // beside it about 21 days. Only the second one decides whether the situation is recoverable,
        // and it is read *inside* this branch rather than before it on purpose — a lapsed refresh
        // token beside an access token that is still valid is not a problem at all, because the
        // request ACT is about to make uses the access token. Checking it first would blank a working
        // meter for the last few hours of a login that is about to need renewing anyway.
        if (Expires(oauth, "expiresAt", now))
        {
            // Absent on a CLI old enough not to write it, which reads as "no reason to think it has
            // lapsed" — the nudge then costs one free process and the truth arrives next poll.
            return Expires(oauth, "refreshTokenExpiresAt", now)
                ? UsageToken.Lapsed
                : UsageToken.Expired;
        }

        return Text(oauth, "accessToken") is { Length: > 0 } token
            ? UsageToken.Present(token)
            : UsageToken.Missing;
    }

    public AgentUsage? Parse(string response, DateTimeOffset takenAt)
    {
        if (Document(response) is not { } root)
            return null;

        var limits = FromLimits(root);
        var named = FromNamedWindows(root);

        // Either surface identifies the body as a usage response; only an unrecognisable one is nothing.
        if (limits is null && named is null)
            return null;

        // `limits` first, so it wins per window where both describe the same one — `kind` names the
        // window explicitly and `percent` is unambiguously 0–100. The named pair fills what it omits.
        var pair = Pair([.. limits ?? [], .. named ?? []]);

        return new AgentUsage(Agent, pair, takenAt, pair.Any(window => window.Percent >= 100), null);
    }

    // A read that succeeded always answers with both account windows. Whether an entry is in the body is
    // a property of the account's activity, not of what ACT could see: the 5-hour clock starts on the
    // first request, and a missing entry means nothing has run — which is a meter reading 0%, not a
    // reason to leave a gap where the session quota should be.
    private static IReadOnlyList<UsageWindow> Pair(IReadOnlyList<UsageWindow> windows)
        =>
        [
            windows.FirstOrDefault(window => window.Kind is UsageWindowKind.Session) ?? Idle(UsageWindowKind.Session),
            windows.FirstOrDefault(window => window.Kind is UsageWindowKind.Weekly) ?? Idle(UsageWindowKind.Weekly),
        ];

    private static UsageWindow Idle(UsageWindowKind kind) => new(kind, 0, null);

    private static IReadOnlyList<UsageWindow>? FromLimits(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind is not JsonValueKind.Array)
            return null;

        var windows = new List<UsageWindow>();

        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind is not JsonValueKind.Object)
                continue;

            var kind = Text(limit, "kind") switch
            {
                "session" => UsageWindowKind.Session,
                "weekly_all" => UsageWindowKind.Weekly,
                _ => (UsageWindowKind?)null,
            };

            if (kind is null)
                continue;

            windows.Add(new UsageWindow(kind.Value, Percent(limit, "percent"), Moment(limit, "resets_at")));
        }

        return windows;
    }

    private static IReadOnlyList<UsageWindow>? FromNamedWindows(JsonElement root)
    {
        var windows = new List<UsageWindow>();

        Add(windows, root, "five_hour", UsageWindowKind.Session);
        Add(windows, root, "seven_day", UsageWindowKind.Weekly);

        return windows.Count > 0 ? windows : null;
    }

    private static void Add(List<UsageWindow> windows, JsonElement root, string name, UsageWindowKind kind)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind is not JsonValueKind.Object)
            return;

        windows.Add(new UsageWindow(kind, Percent(window, "utilization"), Moment(window, "resets_at")));
    }

    private static JsonElement? Document(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Moment(JsonElement element, string name)
        => Text(element, name) is { } text
            && DateTimeOffset.TryParse(text, null, DateTimeStyles.RoundtripKind, out var moment)
                ? moment
                : null;

    private static int Percent(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind is not JsonValueKind.Number
            || !value.TryGetDouble(out var number))
            return 0;

        return (int)Math.Clamp(Math.Round(number), 0, 100);
    }

    private static bool Expires(JsonElement element, string name, DateTimeOffset now)
        => Number(element, name) is { } milliseconds
            && DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) <= now;

    private static long? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
