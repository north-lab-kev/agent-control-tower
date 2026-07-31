using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Rules;

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

    // Ready is the launch; Executing is a re-attach (a card keeps its binding when ACT restarts
    // but not its process); Your turn is the retry the spec promises for an `error`, and without it
    // a failed launch would be a dead end — the failure itself moves the card there. Preparing and
    // Completed are refused: the terminal is reachable from any card now that the two faces toggle,
    // and reaching it must not become a way to skip Ready.
    public bool CanLaunch(Card card)
        => card.Column is BoardColumn.Ready or BoardColumn.Executing or BoardColumn.YourTurn
            && byAgent.ContainsKey(card.AgentType);

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

        if (!CanLaunch(card))
            return LaunchResult.Refused($"A card in {card.Column} cannot be launched.");

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
                        AutoGitInstruction.Append(card.InitialPrompt, card.AutoGit),
                        card.LaunchConfig,
                        size),
                    cancellationToken)
                : await adapter.ResumeAsync(
                    new AgentResumeRequest(
                        card.Id,
                        card.SessionId,
                        card.WorkingDir,
                        AutoGitInstruction.Append(card.InitialPrompt, card.AutoGit),
                        null,
                        card.LaunchConfig,
                        size),
                    cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The card must show that the launch failed rather than sitting in Ready looking
            // untouched, so this routes the same way a crash mid-session would.
            card.Column = BoardColumn.YourTurn;
            card.Badge = Badge.Error;
            card.Transitions.Add(new Transition
            {
                At = clock.Now,
                Column = BoardColumn.YourTurn,
                Badge = Badge.Error,
                Reason = TransitionReason.LaunchFailed,
                Note = error.Message,
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
            Reason = resolution.Adjustments.Count == 0
                ? TransitionReason.Launched
                : TransitionReason.LaunchedWithAdjustments,
            Note = Adjustments(resolution),
        });

        await board.UpdateAsync(card, cancellationToken);

        return LaunchResult.Ok();
    }

    // A restore is not a launch. The process died — with ACT, with a kill, with the CLI's own exit,
    // with the sign-off that ended it — while the binding in the store did not, so the session id
    // comes back into a fresh terminal and the card stays exactly where it was. Moving it to
    // Executing would claim work is running when all that is running is a prompt waiting for its
    // user, and a Completed card reopened to read its history is not work being resumed; the resume
    // carries no message for the same reason.
    public async Task<LaunchResult> RestoreAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
    {
        if (registry.IsLive(card.Id))
            return LaunchResult.Ok();

        if (!SessionRestore.IsResumable(card))
            return LaunchResult.Refused($"A card in {card.Column} with no session cannot be restored.");

        if (!byAgent.TryGetValue(card.AgentType, out var adapter))
            return LaunchResult.Refused($"No adapter is registered for {card.AgentType}.");

        var resolution = adapter.Resolve(card.LaunchConfig);
        if (!resolution.CanLaunch)
            return LaunchResult.Refused(string.Join(" ", resolution.Rejections));

        IAgentSession session;

        try
        {
            session = await adapter.ResumeAsync(
                new AgentResumeRequest(
                    card.Id,
                    card.SessionId!,
                    card.WorkingDir,
                    AutoGitInstruction.Append(card.InitialPrompt, card.AutoGit),
                    null,
                    card.LaunchConfig,
                    size),
                cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Unlike a failed launch this moves nothing: the card is already where the rules put it,
            // and a terminal that could not be brought back is not a new failure of the work. It is
            // recorded so the timeline answers why the terminal is empty.
            card.Transitions.Add(new Transition
            {
                At = clock.Now,
                Column = card.Column,
                Badge = card.Badge,
                Reason = TransitionReason.RestoreFailed,
                Note = error.Message,
            });

            await board.UpdateAsync(card, cancellationToken);

            return LaunchResult.Refused(error.Message);
        }

        registry.Add(session);

        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = card.Column,
            Badge = card.Badge,
            Reason = TransitionReason.SessionRestored,
        });

        await board.UpdateAsync(card, cancellationToken);

        return LaunchResult.Ok();
    }

    // Adjustments are recorded on the card rather than only shown once, so "why is this running
    // at a different effort than I asked for" stays answerable later. The field and value names are
    // the agent's own vocabulary, so they stay verbatim; the sentence around them is the UI's, and
    // it comes from `TransitionReason.LaunchedWithAdjustments` at render time.
    private static string? Adjustments(LaunchConfigResolution resolution)
        => resolution.Adjustments.Count == 0
            ? null
            : string.Join(
                "; ",
                resolution.Adjustments.Select(a => $"{a.Field} {a.Requested} → {a.Substituted}"));
}

public sealed record LaunchResult(bool Launched, string? Message)
{
    public static LaunchResult Ok() => new(true, null);

    public static LaunchResult Refused(string message) => new(false, message);
}
