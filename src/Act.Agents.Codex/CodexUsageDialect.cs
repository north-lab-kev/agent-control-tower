using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.Codex;

public sealed class CodexUsageDialect : IUsageDialect
{
    private const string FileName = "auth.json";

    public AgentType Agent => AgentType.Codex;

    public string DefaultEndpoint => "https://chatgpt.com/backend-api/wham/usage";

    public string DefaultCredentialsPath()
        => Path.Combine(CodexHookConfig.ResolveCodexHome(), FileName);

    public UsageToken Token(string credentials, DateTimeOffset now)
    {
        if (Document(credentials) is not { } root)
            return UsageToken.Missing;

        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind is not JsonValueKind.Object)
            return UsageToken.Missing;

        return Text(tokens, "access_token") is { Length: > 0 } token
            ? UsageToken.Present(token)
            : UsageToken.Missing;
    }

    public AgentUsage? Parse(string response, DateTimeOffset takenAt)
    {
        if (Document(response) is not { } root)
            return null;

        if (!root.TryGetProperty("rate_limit", out var limit) || limit.ValueKind is not JsonValueKind.Object)
            return null;

        var windows = new List<UsageWindow>();

        Add(windows, limit, "primary_window", takenAt);
        Add(windows, limit, "secondary_window", takenAt);

        if (windows.Count == 0)
            return null;

        return new AgentUsage(Agent, windows, takenAt, Flag(limit, "limit_reached"), Text(root, "plan_type"));
    }

    private static void Add(List<UsageWindow> windows, JsonElement limit, string name, DateTimeOffset takenAt)
    {
        if (!limit.TryGetProperty(name, out var window) || window.ValueKind is not JsonValueKind.Object)
            return;

        if (Number(window, "limit_window_seconds") is not { } seconds)
            return;

        if (Resets(window, takenAt) is not { } resetsAt)
            return;

        var length = TimeSpan.FromSeconds(seconds);

        windows.Add(new UsageWindow(
            UsageWindow.Classify(length),
            Percent(window, "used_percent"),
            resetsAt));
    }

    private static DateTimeOffset? Resets(JsonElement window, DateTimeOffset takenAt)
    {
        if (Number(window, "reset_after_seconds") is { } after)
            return takenAt.AddSeconds(after);

        if (Number(window, "reset_at") is { } at)
            return DateTimeOffset.FromUnixTimeSeconds(at);

        return null;
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

    private static bool Flag(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
