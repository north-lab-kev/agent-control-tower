using System.Text.Json;
using System.Text.Json.Nodes;
using Act.Core.Agents;

namespace Act.Agents.ClaudeCode;

// The `--settings` file ACT hands Claude Code at launch. It is written per launch into ACT's own
// data directory and passed by path, so the user's committed `.claude/settings.json` is never
// read, merged or overwritten by ACT.
//
// Every hook here is observability-only: `type: "http"`, and ACT ignores whatever it answers.
// None of them can deny a tool, block a turn, or fail a session.
//
// It also carries the pre-allow list for ACT's own MCP tools (declared in `ClaudeCodeMcpConfig`),
// which is the one thing in this file that is not observability. It grants only ACT's three tool
// ids and never widens anything else: an approval prompt for a tool ACT itself installed would stop
// an unattended card on a question the user never asked to be asked.
public static class ClaudeCodeHookSettings
{
    public const string FileName = "act-settings.json";

    // `Notification` is load-bearing rather than incidental: it is the only signal that reports a
    // permission prompt now that ACT does not read the screen. `PreToolUse` is load-bearing for the
    // same reason — a question to the user reaches ACT only as the tool about to run, never as the
    // notification. See the normalizer for what each one is measured to carry.
    private static readonly string[] Events =
    [
        "SessionStart",
        "SessionEnd",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "Notification",
        "PreCompact",
        "Stop",
    ];

    public static string Compose(Uri endpoint, string token)
    {
        var hooks = new JsonObject();

        foreach (var name in Events)
        {
            hooks[name] = new JsonArray(
                new JsonObject
                {
                    ["hooks"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "http",
                            ["url"] = endpoint.ToString(),
                            ["headers"] = new JsonObject
                            {
                                [HookTransport.TokenHeader] = token,
                            },
                        }),
                });
        }

        var allow = new JsonArray();

        foreach (var tool in McpTransport.Tools)
            allow.Add(McpTransport.PermissionId(tool));

        var settings = new JsonObject
        {
            ["hooks"] = hooks,
            ["permissions"] = new JsonObject
            {
                ["allow"] = allow,
            },
        };

        return settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
