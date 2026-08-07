namespace Act.Core.Agents;

// The wire details both sides of the MCP channel have to agree on, beside `HookTransport` and for
// the same reason: the adapters write these into agent config and cannot reference infrastructure,
// and a name that drifts between the writer and the reader fails as a tool the model never sees.
//
// The token header is shared with the hooks deliberately — one endpoint, one per-session secret,
// one task-resolution path.
public static class McpTransport
{
    public const string Route = "/mcp";

    public const string ServerName = "act";

    public const string CreateFollowUp = "create_followup";

    public const string ListTasks = "list_tasks";

    public const string GetTask = "get_task";

    public static IReadOnlyList<string> Tools => [CreateFollowUp, ListTasks, GetTask];

    // How a CLI names one of these in its own permission list: `mcp__act__create_followup`.
    public static string PermissionId(string tool) => $"mcp__{ServerName}__{tool}";
}
