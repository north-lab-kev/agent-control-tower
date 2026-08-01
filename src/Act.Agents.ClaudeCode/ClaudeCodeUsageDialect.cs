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

        if (Number(oauth, "expiresAt") is { } expiresAt
            && DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) <= now)
            return UsageToken.Expired;

        return Text(oauth, "accessToken") is { Length: > 0 } token
            ? UsageToken.Present(token)
            : UsageToken.Missing;
    }

    public AgentUsage? Parse(string response, DateTimeOffset takenAt)
    {
        if (Document(response) is not { } root)
            return null;

        var windows = FromLimits(root) ?? FromNamedWindows(root);

        if (windows.Count == 0)
            return null;

        return new AgentUsage(Agent, windows, takenAt, windows.Any(window => window.Percent >= 100), null);
    }

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

            if (kind is null || Moment(limit, "resets_at") is not { } resetsAt)
                continue;

            windows.Add(new UsageWindow(kind.Value, Percent(limit, "percent"), resetsAt));
        }

        return windows.Count > 0 ? windows : null;
    }

    private static IReadOnlyList<UsageWindow> FromNamedWindows(JsonElement root)
    {
        var windows = new List<UsageWindow>();

        Add(windows, root, "five_hour", UsageWindowKind.Session);
        Add(windows, root, "seven_day", UsageWindowKind.Weekly);

        return windows;
    }

    private static void Add(List<UsageWindow> windows, JsonElement root, string name, UsageWindowKind kind)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind is not JsonValueKind.Object)
            return;

        if (Moment(window, "resets_at") is not { } resetsAt)
            return;

        windows.Add(new UsageWindow(kind, Percent(window, "utilization"), resetsAt));
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
