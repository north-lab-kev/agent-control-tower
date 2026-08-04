using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

public sealed class ClaudeCodeUsageRefresher(
    ICommandHost commands,
    IAgentConfigFiles configFiles,
    Func<AgentDefaults?> machine) : IUsageRefresher
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    public AgentType Agent => AgentType.ClaudeCode;

    // PENDING MEASUREMENT: `auth status` is the cheapest command that reads the credential file at
    // all — free, and 0 s against 2.1.220 — but that it *also* performs the refresh-on-use exchange
    // for an expired token has not been observed directly; only that the CLI refreshes on use.
    // See the re-test checklist in `docs/agent-usage-findings.md`. If it turns out not to refresh,
    // the fix is this argument list, not the seam.
    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        var install = machine();

        try
        {
            var result = await commands.RunAsync(
                new CommandStartInfo(
                    install?.Binary is { Length: > 0 } binary ? binary.Trim() : ClaudeCodeAdapter.DefaultBinary,
                    ["auth", "status", "--json"],
                    configFiles.ScratchDirectory(),
                    AgentEnvironment.ForQuery(install?.Env ?? new Dictionary<string, string>()),
                    Timeout: Deadline),
                cancellationToken);

            return result.Succeeded;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }
}
