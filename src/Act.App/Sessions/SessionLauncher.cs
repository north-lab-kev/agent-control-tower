using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.App.Sessions;

// Ready → Executing. The one transition that is an ACT *action* rather than a rule, which is
// why it lives here and not in the rules engine.
public sealed class SessionLauncher(
    IEnumerable<IAgentAdapter> adapters,
    SessionRegistry registry,
    BoardState board,
    IClock clock)
{
    private readonly IReadOnlyDictionary<AgentType, IAgentAdapter> byAgent =
        adapters.ToDictionary(adapter => adapter.Agent);

    public bool CanLaunch(Card card)
        => card.Column == BoardColumn.Ready && byAgent.ContainsKey(card.AgentType);

    public LaunchConfigResolution? Preview(Card card)
        => byAgent.TryGetValue(card.AgentType, out var adapter)
            ? adapter.Resolve(card.LaunchConfig)
            : null;

    public async Task<LaunchResult> LaunchAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
    {
        if (registry.IsLive(card.Id))
            return LaunchResult.Ok();

        if (!byAgent.TryGetValue(card.AgentType, out var adapter))
            return LaunchResult.Refused($"No adapter is registered for {card.AgentType}.");

        var resolution = adapter.Resolve(card.LaunchConfig);
        if (!resolution.CanLaunch)
            return LaunchResult.Refused(string.Join(" ", resolution.Rejections));

        // Pre-minted here so Claude Code can be handed it; Codex ignores it and reports its own
        // in `SessionStart`, which is why the card's copy is only written when it is real.
        var sessionId = card.SessionId ?? Guid.NewGuid().ToString();

        IAgentSession session;

        try
        {
            session = card.SessionId is null
                ? await adapter.LaunchAsync(
                    new AgentLaunchRequest(
                        card.Id,
                        sessionId,
                        card.WorkingDir,
                        AgentPreamble.Compose(card.Id, card.AutoComplete ? card.AutoGit : null),
                        card.InitialPrompt,
                        card.LaunchConfig,
                        size),
                    cancellationToken)
                : await adapter.ResumeAsync(
                    new AgentResumeRequest(
                        card.Id,
                        card.SessionId,
                        card.WorkingDir,
                        AgentPreamble.Compose(card.Id, card.AutoComplete ? card.AutoGit : null),
                        card.InitialPrompt,
                        null,
                        card.LaunchConfig,
                        size),
                    cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The card must show that the launch failed rather than sitting in Ready looking
            // untouched, so this routes the same way a crash mid-session would.
            card.Column = BoardColumn.NeedsFeedback;
            card.Badge = Badge.Error;
            card.Transitions.Add(new Transition
            {
                At = clock.Now,
                Column = BoardColumn.NeedsFeedback,
                Badge = Badge.Error,
                Note = $"Launch failed: {error.Message}",
            });

            await board.UpdateAsync(card, cancellationToken);

            return LaunchResult.Refused(error.Message);
        }

        registry.Add(session);

        card.SessionId = session.SessionId;
        card.Column = BoardColumn.Executing;
        card.Badge = Badge.Running;
        card.LaunchedAt ??= clock.Now;
        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = BoardColumn.Executing,
            Badge = Badge.Running,
            Note = Note(resolution),
        });

        await board.UpdateAsync(card, cancellationToken);

        return LaunchResult.Ok();
    }

    // Adjustments are recorded on the card rather than only shown once, so "why is this running
    // at a different effort than I asked for" stays answerable later.
    private static string? Note(LaunchConfigResolution resolution)
        => resolution.Adjustments.Count == 0
            ? null
            : "Launched with adjustments: " + string.Join(
                "; ",
                resolution.Adjustments.Select(a => $"{a.Field} {a.Requested} → {a.Substituted}"));
}

public sealed record LaunchResult(bool Launched, string? Message)
{
    public static LaunchResult Ok() => new(true, null);

    public static LaunchResult Refused(string message) => new(false, message);
}
