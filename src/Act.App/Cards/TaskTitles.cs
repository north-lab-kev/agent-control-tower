using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.App.Cards;

// A title from the task's own prompt, asked of the task's own agent. The point of the feature is
// that nobody should have to name a task twice — the prompt already says what it is — so this runs
// both when the user asks for it and when they save without a title.
//
// It never fails. `TaskTitleQuery.FromPrompt` is the floor: if the CLI is missing, refuses, times out
// or answers with something unusable, the user still gets a title and their save still goes through.
// The board has no room for an untitled card, and a save refused over a field ACT offered to fill in
// would be the worst of both designs.
public sealed class TaskTitles(
    IEnumerable<IAgentAdapter> adapters,
    UserSettingsService settings,
    ILogger<TaskTitles> log)
{
    private readonly IReadOnlyDictionary<AgentType, IAgentAdapter> byAgent =
        adapters.ToDictionary(adapter => adapter.Agent);

    // A title, and whether an agent is what produced it — the caller needs the difference to decide
    // whether to say anything, not to decide what to store.
    public sealed record Result(string Title, bool Generated);

    // Null only for a prompt there is nothing to make a title out of, which the caller has to handle
    // anyway — the form refuses to save without a prompt.
    public async Task<Result?> SuggestAsync(
        string prompt,
        AgentType agent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        if (await AskAsync(prompt, agent, cancellationToken) is { Length: > 0 } generated)
            return new Result(generated, Generated: true);

        return TaskTitleQuery.FromPrompt(prompt) is { Length: > 0 } excerpt
            ? new Result(excerpt, Generated: false)
            : null;
    }

    private async Task<string?> AskAsync(string prompt, AgentType agent, CancellationToken cancellationToken)
    {
        if (!byAgent.TryGetValue(agent, out var adapter))
            return null;

        try
        {
            var answer = await adapter.QueryAsync(
                new AgentQueryRequest(TaskTitleQuery.PromptFor(prompt), settings.Defaults(agent)),
                cancellationToken);

            return TaskTitleQuery.Clean(answer);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Logged, never shown: the user asked for a title, not for a diagnosis of their install.
            log.LogWarning(error, "Title generation failed for {Agent}.", agent);

            return null;
        }
    }
}
