using Act.App.Cards;
using Act.App.Notifications;
using Act.App.Resources;
using Act.App.Settings;
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
    NotificationDispatcher notifications,
    UserSettingsService settings,
    IWorkingDirectories directories,
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
            ? adapter.Resolve(Config(card))
            : null;

    // The card says what to run; this machine's settings say where the CLI is and what it always
    // gets. Every path to an adapter goes through here, so there is one place the two are joined.
    private LaunchConfig Config(Card card)
        => LaunchComposition.Compose(card.LaunchConfig, settings.Defaults(card.AgentType));

    // The card already working in this card's folder, if the guard is on and this card is not
    // exempt from it. `board.All` rather than a column query: the rule decides for itself what
    // holding a folder means, and asking it about the whole store keeps that decision in one place.
    private Card? Blocking(Card card)
        => WorkingDirConflict.Blocking(card, board.All, directories, settings.PreventConcurrentWorkingDir);

    // A failed card is only retriable while its process is gone: a CLI that is still alive is one
    // the user can type into, and ACT types nothing into a live agent.
    public bool CanRetry(Card card) => CardRetry.CanRetry(card, registry.IsLive(card.Id));

    // Retry is a launch that carries a message. Not the initial prompt — a session forty turns deep
    // would be told to start over — but a short "you were interrupted, continue", which rides the
    // resume command line exactly as the opening prompt rides the launch one.
    public Task<LaunchResult> RetryAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
        => CanRetry(card)
            ? StartAsync(card, size, RetryInstruction.Message, cancellationToken)
            : Task.FromResult(LaunchResult.Refused($"A card in {card.Column} cannot be retried."));

    public Task<LaunchResult> LaunchAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
        => StartAsync(card, size, resumeMessage: null, cancellationToken);

    private async Task<LaunchResult> StartAsync(
        Card card,
        TerminalSize size,
        string? resumeMessage,
        CancellationToken cancellationToken = default)
    {
        if (registry.IsLive(card.Id))
            return LaunchResult.Ok();

        if (!CanLaunch(card))
            return LaunchResult.Refused($"A card in {card.Column} cannot be launched.");

        // Before anything is spawned and before the card is moved, so a refused launch leaves it
        // sitting in Ready exactly as it was — the folder frees up on its own, and nothing here
        // needs undoing when it does.
        if (Blocking(card) is { } holder)
            return LaunchResult.Wait(
                Text.Format(Strings.Launch_WorkingDirBusy, holder.Number, holder.Title));

        if (!byAgent.TryGetValue(card.AgentType, out var adapter))
            return LaunchResult.Refused($"No adapter is registered for {card.AgentType}.");

        var config = Config(card);
        var resolution = adapter.Resolve(config);
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
                        config,
                        size),
                    cancellationToken)
                : await adapter.ResumeAsync(
                    new AgentResumeRequest(
                        card.Id,
                        card.SessionId,
                        card.WorkingDir,
                        AutoGitInstruction.Append(card.InitialPrompt, card.AutoGit),
                        resumeMessage,
                        config,
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

            notifications.Notify(card);

            return LaunchResult.Refused(error.Message);
        }

        registry.Add(session);

        card.SessionId = session.SessionId;
        card.Column = BoardColumn.Executing;
        card.Badge = Badge.Running;
        card.LaunchedAt ??= clock.Now;

        // The armed instant belongs to the wait, not to the card: left behind, a card dragged back
        // to Ready would still be pointing at a window boundary that passed while it was running.
        card.EligibleAt = null;
        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = BoardColumn.Executing,
            Badge = Badge.Running,
            Reason = (resumeMessage is null, resolution.Adjustments.Count == 0) switch
            {
                (true, true) => TransitionReason.Launched,
                (true, false) => TransitionReason.LaunchedWithAdjustments,
                (false, true) => TransitionReason.Retried,
                _ => TransitionReason.RetriedWithAdjustments,
            },
            Note = Adjustments(resolution),
        });

        await board.UpdateAsync(card, cancellationToken);

        return LaunchResult.Ok();
    }

    // A restore is not a launch. The process died — with ACT, with the CLI's own exit, with the
    // sign-off that ended it — while the binding in the store did not, so the session id
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

        return await ResumeAsync(card, size, TransitionReason.SessionRestored, cancellationToken);
    }

    public bool CanRestart(Card card) => SessionRestore.IsResumable(card) && byAgent.ContainsKey(card.AgentType);

    // The terminal, not the work. A TUI can become unusable while the session behind it is perfectly
    // healthy — a wedged or garbled screen, a CLI that stopped painting — and the only remedy used to
    // be ending a session that was never the problem. So this tears the pty down and brings the *same*
    // session id straight back into a fresh one: the card keeps its column, its badge and its binding,
    // because resuming a session is not a claim about the work.
    //
    // Unlike `RestoreAsync` it does not bail on a live session — a live-but-useless one is the entire
    // reason it exists.
    public async Task<LaunchResult> RestartAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
    {
        if (!CanRestart(card))
            return LaunchResult.Refused($"A card in {card.Column} with no session cannot be restarted.");

        await registry.EndAsync(card.Id);

        return await ResumeAsync(card, size, TransitionReason.TerminalRestarted, cancellationToken);
    }

    private async Task<LaunchResult> ResumeAsync(
        Card card,
        TerminalSize size,
        TransitionReason reason,
        CancellationToken cancellationToken)
    {
        if (!SessionRestore.IsResumable(card))
            return LaunchResult.Refused($"A card in {card.Column} with no session cannot be restored.");

        if (!byAgent.TryGetValue(card.AgentType, out var adapter))
            return LaunchResult.Refused($"No adapter is registered for {card.AgentType}.");

        var config = Config(card);
        var resolution = adapter.Resolve(config);
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
                    config,
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
            Reason = reason,
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

public sealed record LaunchResult(bool Launched, string? Message, bool Waiting = false)
{
    public static LaunchResult Ok() => new(true, null);

    public static LaunchResult Refused(string message) => new(false, message);

    // Refused, but nothing is broken: the card, the prompt and the install are all fine and the
    // thing in the way clears on its own. Red would claim a failure the user then goes looking for.
    public static LaunchResult Wait(string message) => new(false, message, Waiting: true);
}
