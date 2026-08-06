using System.ComponentModel;
using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Spawning;
using ModelContextProtocol.Server;

namespace Act.App.Mcp;

// The whole agent→ACT contract: one write and two reads. Everything decidable without a web
// framework is behind `FollowUpService` and `Act.Core/Spawning`; what is here is the schema the
// model sees and the token that says who is calling.
//
// **The descriptions are English on purpose**, unlike every other string ACT sends an agent. They
// are read by a model rather than by the user, and an argument vocabulary that changed with the UI
// language would be a different API per locale.
//
// **The token is read off the request, never taken as a parameter.** The calling task is a fact
// about the connection, which is what makes `parentId` unnecessary and un-spoofable — and a tool
// argument would be both visible to the model and suppliable by it.
//
// `[McpHeader]` looked like the way to express that and is not: measured on SDK 2.1.0, a parameter
// carrying it is still published in the tool's input schema, so the model saw a `token` argument it
// could not supply and every call failed. `FollowUpTests` pins the schema against that returning.
[McpServerToolType]
public sealed class ActMcpTools(
    FollowUpService followUps,
    IHookEndpoint hooks,
    IHttpContextAccessor context)
{
    // What a client reads before it has fetched a single tool schema. Load-bearing rather than
    // framing: with tool search enabled Claude Code loads only tool *names* and this text at session
    // start, so the first 512 characters have to stand alone — which is also Codex's documented
    // cutoff for the same field.
    public const string Instructions =
        "ACT is the control tower running this session. Its board holds one card per task, and this "
        + "server is the only way to write to it. Use create_followup when work comes up that belongs "
        + "in its own task rather than this one — a fix outside this task's scope, a second phase, a "
        + "cleanup — instead of deferring it in your final message, where it is lost. The new card "
        + "lands in Ready for the user to launch; nothing starts on its own. Use list_tasks and "
        + "get_task to check whether a task already exists before creating a duplicate, and to find "
        + "the ids that dependsOn takes.\n\n"
        + "Everything omitted from create_followup is inherited from this task, so a follow-up in the "
        + "same repo with the same settings needs only a title and a prompt. Do not use it to split "
        + "work you were asked to finish here.";

    [McpServerTool(Name = McpTransport.CreateFollowUp, Destructive = false, OpenWorld = false)]
    [Description(
        "Create a follow-up task on ACT's board for work that belongs in its own task rather than "
        + "this one. The card lands in the Ready column for the user to launch; it does not start "
        + "anything. Returns the new task's id and number, and the settings it actually got.")]
    public async Task<object> CreateFollowUpAsync(
        [Description("Short imperative title for the board, e.g. \"Migrate the auth tests\".")]
        string title,

        [Description(
            "The full instructions the agent running this task will receive. Self-contained: it will "
            + "not see this conversation.")]
        string prompt,

        [Description(
            "Which CLI runs it: \"same\" (default, this task's), \"claude\" or \"codex\". Hand work to "
            + "the other agent when it genuinely suits it better.")]
        string? agent = null,

        [Description("Model slug for the chosen agent, or \"same\" (default). Unknown values are refused with the list.")]
        string? model = null,

        [Description("Reasoning effort for the chosen agent, or \"same\" (default).")]
        string? effort = null,

        [Description(
            "Permission mode: \"same\" (default), \"default\", \"plan\", \"acceptEdits\", \"auto\", "
            + "\"dontAsk\" or \"bypass\".")]
        string? permission = null,

        [Description(
            "When it may launch: \"manual\" (default, waits for the user), \"now\" (queued "
            + "immediately) or \"next_window\". There is no \"same\" — this task's schedule already "
            + "fired.")]
        string? schedule = null,

        [Description("Git action to perform in-session: \"same\" (default), \"none\", \"commit\", \"push\" or \"pr\".")]
        string? autoGit = null,

        [Description("Working directory. Defaults to this task's.")]
        string? cwd = null,

        [Description(
            "Task ids that must be signed off before this one may launch. Use ids from this tool's "
            + "own results or from list_tasks.")]
        string[]? dependsOn = null,

        [Description(
            "Optional idempotency key. Calling twice with the same key under this task returns the "
            + "first card instead of creating a second.")]
        string? clientKey = null,
        CancellationToken cancellationToken = default)
    {
        if (Caller() is not { } parentId)
            return Failure("This session is not authorized to create follow-ups.");

        var outcome = await followUps.CreateAsync(
            parentId,
            new FollowUpRequest(
                Title: title,
                Prompt: prompt,
                Agent: agent,
                Model: model,
                Effort: effort,
                Permission: permission,
                Schedule: schedule,
                AutoGit: autoGit,
                WorkingDir: cwd,
                DependsOn: dependsOn,
                ClientKey: clientKey),
            cancellationToken);

        return outcome.Created is { } created ? created : Failure([.. outcome.Refusals]);
    }

    [McpServerTool(Name = McpTransport.ListTasks, ReadOnly = true, OpenWorld = false)]
    [Description(
        "List the tasks on ACT's board — every card, not only this task's own follow-ups. Summaries "
        + "only; call get_task for one task's full detail. Use it to avoid creating a duplicate and "
        + "to find ids for dependsOn.")]
    public object ListTasks(
        [Description(
            "Optional column filter: \"preparing\", \"ready\", \"executing\", \"your_turn\" or "
            + "\"completed\".")]
        string? column = null)
        => Caller() is { } callerId
            ? followUps.List(callerId, column)
            : Failure("This session is not authorized to read the board.");

    [McpServerTool(Name = McpTransport.GetTask, ReadOnly = true, OpenWorld = false)]
    [Description("Full detail for one task on ACT's board, by id — including its prompt, settings and lineage.")]
    public object GetTask(
        [Description("The task id, as returned by create_followup or list_tasks.")] string id)
    {
        if (Caller() is not { } callerId)
            return Failure("This session is not authorized to read the board.");

        return followUps.Detail(callerId, id) is { } detail
            ? detail
            : Failure($"No task on the board has id '{id}'.");
    }

    // Resolved on every call rather than captured when the connection opened. A streamable-HTTP
    // session outlives many requests, and ending a card releases its token — read once at initialize
    // and a finished card would keep a working connection for as long as the client held the socket.
    private Guid? Caller()
    {
        var token = context.HttpContext?.Request.Headers[HookTransport.TokenHeader].ToString();

        return token is { Length: > 0 } supplied && hooks.TryResolve(supplied, out var taskId)
            ? taskId
            : null;
    }

    // A refusal an agent can act on, not an exception: a thrown tool call reaches the model as a
    // transport failure, and "you asked for a model that does not exist" is information it can use.
    private static object Failure(params string[] errors) => new { ok = false, errors };
}
