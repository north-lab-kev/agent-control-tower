using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

public sealed class ClaudeCodeUsageRefresher(
    Func<IAgentAdapter> adapter,
    Func<AgentDefaults?> machine) : IUsageRefresher
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    private const string Prompt = "Reply with exactly one word: ok";

    public AgentType Agent => AgentType.ClaudeCode;

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await adapter().QueryAsync(
                new AgentQueryRequest(Prompt, machine()) { Timeout = Deadline },
                cancellationToken);

            return answer is { Length: > 0 };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }
}
