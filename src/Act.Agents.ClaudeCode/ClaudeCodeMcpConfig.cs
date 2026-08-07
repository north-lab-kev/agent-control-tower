using System.Text.Json;
using System.Text.Json.Nodes;
using Act.Core.Agents;

namespace Act.Agents.ClaudeCode;

// The `--mcp-config` file ACT hands Claude Code at launch, declaring ACT's own server and nothing
// else. Written per launch into ACT's data directory and passed by path, so the user's `.mcp.json`
// is never read, merged or overwritten.
//
// **No `--strict-mcp-config`.** It would make ACT's server the *only* one the session has, silently
// taking away every MCP tool the user has configured for their own work. ACT adds a server; it does
// not curate the user's.
//
// The token is a static header here, unlike Codex's environment indirection, and that is safe for the
// same reason the hook settings file holds one: this file is per launch and per task, so there is no
// definition hash to keep stable.
public static class ClaudeCodeMcpConfig
{
    public const string FileName = "act-mcp.json";

    public static string Compose(Uri endpoint, string token)
    {
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [McpTransport.ServerName] = new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = new Uri(endpoint, McpTransport.Route).ToString(),
                    ["headers"] = new JsonObject
                    {
                        [HookTransport.TokenHeader] = token,
                    },
                },
            },
        };

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
