using Act.App.Cards;
using Act.App.Notifications;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// Ready → Executing. The one transition that is an ACT *action* rather than a rule, which is
// why it lives here and not in the rules engine.
//
// Four entry points — launch, retry, restore, restart — and they are four sets of *preconditions*
// over one body. What separates them past those preconditions is a single question, `StartKind`:
// whether starting a process is a claim about the work. Everything after that answer is shared, and
// `BeginAsync` is where it lives.
public sealed class SessionLauncher(
    IEnumerable<IAgentAdapter> adapters,
    SessionRegistry registry,
    BoardState board,
    NotificationDispatcher notifications,
    UserSettingsService settings,
    IWorkingDirectories directories,
    IClock clock,
    ILogger<SessionLauncher> log)
{
    private readonly IReadOnlyDictionary<AgentType, IAgentAdapter> byAgent =
        adapters.ToDictionary(adapter => adapter.Agent);

    // Whether a start says anything about the work, and the only axis the entry points differ on.
    //
    // A **launch** claims the card: it moves to Executing, stamps `launchedAt`, and if the spawn
    // fails the card wears that as `error`, because the work really was supposed to start.
    //
    // A **resume** claims nothing. The process died — with ACT, with the CLI's own exit, with the
    // sign-off that ended it — and is coming back into a fresh terminal; moving the card would say
    // work is running when all that is running is a prompt waiting for its user, and a terminal that
    // could not be brought back is not a new failure of the work.
    private enum StartKind
    {
        Launch,
        Resume,
    }

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

    // A failed card is only retriable while its process is gone: a CLI that is still alive is one
    // the user can type into, and ACT types nothing into a live agent.
    public bool CanRetry(Card card) => CardRetry.CanRetry(card, registry.IsLive(card.Id));

    public bool CanRestart(Card card) => SessionRestore.IsResumable(card) && byAgent.ContainsKey(card.AgentType);

    public Task<LaunchResult> LaunchAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
        => StartAsync(card, size, resumeMessage: null, cancellationToken);

    // Retry is a launch that carries a message. Not the initial prompt — a session forty turns deep
    // would be told to start over — but a short "you were interrupted, continue", which rides the
    // resume command line exactly as the opening prompt rides the launch one.
    public Task<LaunchResult> RetryAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
    {
        if (CanRetry(card))
            return StartAsync(card, size, RetryInstruction.Message, cancellationToken);

        using (log.BeginTaskScope(card.Number, card.SessionId))
            log.LogInformation("Not retried: a card in {Column} cannot be retried.", card.Column);

        return Task.FromResult(LaunchResult.Refused($"A card in {card.Column} cannot be retried."));
    }

    // The card gets its terminal back and stays exactly where it was — see `StartKind.Resume`. A
    // Completed card reopened to read its history is not work being resumed, which is why the
    // resume carries no message.
    public Task<LaunchResult> RestoreAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
        => registry.IsLive(card.Id)
            ? Task.FromResult(LaunchResult.Ok())
            : ResumeAsync(card, size, TransitionReason.SessionRestored, cancellationToken);

    // The terminal, not the work. A TUI can become unusable while the session behind it is perfectly
    // healthy — a wedged or garbled screen, a CLI that stopped painting — and the only remedy used to
    // be ending a session that was never the problem. So this tears the pty down and brings the *same*
    // session id straight back into a fresh one.
    //
    // Unlike `RestoreAsync` it does not bail on a live session — a live-but-useless one is the entire
    // reason it exists.
    public async Task<LaunchResult> RestartAsync(
        Card card,
        TerminalSize size,
        CancellationToken cancellationToken = default)
    {
        if (!CanRestart(card))
        {
            using (log.BeginTaskScope(card.Number, card.SessionId))
                log.LogInformation("Not restarted: a card in {Column} has no session.", card.Column);

            return LaunchResult.Refused($"A card in {card.Column} with no session cannot be restarted.");
        }

        await registry.EndAsync(card.Id);

        return await ResumeAsync(card, size, TransitionReason.TerminalRestarted, cancellationToken);
    }

    // The scope every line below inherits, and the reason these two are `async` rather than
    // Task-returning: a scope disposed when the method returns would be gone before the work it
    // names has started.
    private async Task<LaunchResult> StartAsync(
        Card card,
        TerminalSize size,
        string? resumeMessage,
        CancellationToken cancellationToken)
    {
        using var scope = log.BeginTaskScope(card.Number, card.SessionId);

        if (registry.IsLive(card.Id))
            return LaunchResult.Ok();

        if (!CanLaunch(card))
        {
            log.LogInformation("Not launched: a card in {Column} cannot be launched.", card.Column);

            return LaunchResult.Refused($"A card in {card.Column} cannot be launched.");
        }

        // Before anything is spawned and before the card is moved, so a refused launch leaves it
        // sitting in Ready exactly as it was — the folder frees up on its own, and nothing here
        // needs undoing when it does.
        if (Blocking(card) is { } holder)
        {
            log.LogInformation(
                "Not launched: task {Holder} is still working in {WorkingDir}.",
                holder.Number,
                card.WorkingDir);

            return LaunchResult.Wait(
                Text.Format(Strings.Launch_WorkingDirBusy, holder.Number, holder.Title));
        }

        return await BeginAsync(
            card,
            StartKind.Launch,
            resumeMessage,
            size,
            resolution => Launched(resumeMessage is not null, resolution.Adjustments.Count > 0),
            cancellationToken);
    }

    private async Task<LaunchResult> ResumeAsync(
        Card card,
        TerminalSize size,
        TransitionReason reason,
        CancellationToken cancellationToken)
    {
        using var scope = log.BeginTaskScope(card.Number, card.SessionId);

        if (!SessionRestore.IsResumable(card))
        {
            log.LogInformation("Not resumed: a card in {Column} has no session to resume.", card.Column);

            return LaunchResult.Refused($"A card in {card.Column} with no session cannot be restored.");
        }

        return await BeginAsync(card, StartKind.Resume, message: null, size, _ => reason, cancellationToken);
    }

    // The body all four entry points share: join the card's config to this machine's, ask the adapter
    // to resolve it, spawn, and record what happened. `kind` is the whole of what varies.
    private async Task<LaunchResult> BeginAsync(
        Card card,
        StartKind kind,
        string? message,
        TerminalSize size,
        Func<LaunchConfigResolution, TransitionReason> reason,
        CancellationToken cancellationToken)
    {
        if (!byAgent.TryGetValue(card.AgentType, out var adapter))
        {
            log.LogError("No adapter is registered for {Agent}.", card.AgentType);

            return LaunchResult.Refused($"No adapter is registered for {card.AgentType}.");
        }

        var config = Config(card);
        var resolution = adapter.Resolve(config);

        if (!resolution.CanLaunch)
        {
            log.LogWarning(
                "{Kind} refused for {Agent}: {Rejections}",
                kind,
                card.AgentType,
                string.Join(" ", resolution.Rejections));

            return LaunchResult.Refused(string.Join(" ", resolution.Rejections));
        }

        IAgentSession session;

        try
        {
            session = await SpawnAsync(adapter, card, config, message, size, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return await FailedAsync(card, kind, error, cancellationToken);
        }

        registry.Add(session);

        if (kind is StartKind.Launch)
            Claim(card, session);

        log.LogInformation(
            "{Kind} of {Agent} started in {WorkingDir}, session {StartedSession}.",
            kind,
            card.AgentType,
            card.WorkingDir,
            session.SessionId);

        if (Adjustments(resolution) is { } adjusted)
            log.LogWarning("The launch config was adjusted: {Adjustments}", adjusted);

        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = card.Column,
            Badge = card.Badge,
            Reason = reason(resolution),
            Note = Adjustments(resolution),
        });

        await board.UpdateAsync(card, cancellationToken);

        return LaunchResult.Ok();
    }

    // Which of the adapter's two doors to go through is the *card's* answer, not the caller's: one
    // that has never bound a session is launched, one that has is resumed. `message` is the only
    // thing separating a retry from a bare re-attach, and it is null for both restore and restart.
    private static Task<IAgentSession> SpawnAsync(
        IAgentAdapter adapter,
        Card card,
        LaunchConfig config,
        string? message,
        TerminalSize size,
        CancellationToken cancellationToken)
    {
        var prompt = AutoGitInstruction.Append(card.InitialPrompt, card.AutoGit);

        if (card.SessionId is { } sessionId)
            return adapter.ResumeAsync(
                new AgentResumeRequest(card.Id, sessionId, card.WorkingDir, prompt, message, config, size),
                cancellationToken);

        // Pre-minted so Claude Code can be handed it; Codex ignores it and reports its own in
        // `SessionStart`, which is why the card's copy is only written when it is real — see `Claim`.
        return adapter.LaunchAsync(
            new AgentLaunchRequest(
                card.Id,
                Guid.NewGuid().ToString(),
                card.WorkingDir,
                prompt,
                config,
                size),
            cancellationToken);
    }

    private void Claim(Card card, IAgentSession session)
    {
        card.SessionId = session.SessionId;
        card.Column = BoardColumn.Executing;
        card.Badge = Badge.Running;
        card.LaunchedAt ??= clock.Now;

        // The armed instant belongs to the wait, not to the card: left behind, a card dragged back
        // to Ready would still be pointing at a window boundary that passed while it was running.
        card.EligibleAt = null;
    }

    // A launch that never started is the work failing, so the card shows it rather than sitting in
    // Ready looking untouched — it routes the same way a crash mid-session would. A resume that
    // failed moves nothing. Both are recorded, because the timeline is what answers "why is this
    // pane empty".
    private async Task<LaunchResult> FailedAsync(
        Card card,
        StartKind kind,
        Exception error,
        CancellationToken cancellationToken)
    {
        var claimed = kind is StartKind.Launch;

        log.LogError(error, "{Kind} failed.", kind);

        if (claimed)
        {
            card.Column = BoardColumn.YourTurn;
            card.Badge = Badge.Error;
        }

        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = card.Column,
            Badge = card.Badge,
            Reason = claimed ? TransitionReason.LaunchFailed : TransitionReason.RestoreFailed,
            Note = error.Message,
        });

        await board.UpdateAsync(card, cancellationToken);

        if (claimed)
            notifications.Notify(card);

        return LaunchResult.Refused(error.Message);
    }

    // Four reasons for one event, because the timeline has to say both which kind of start it was
    // and whether the launch got exactly what the card asked for.
    private static TransitionReason Launched(bool retried, bool adjusted) => (retried, adjusted) switch
    {
        (false, false) => TransitionReason.Launched,
        (false, true) => TransitionReason.LaunchedWithAdjustments,
        (true, false) => TransitionReason.Retried,
        _ => TransitionReason.RetriedWithAdjustments,
    };

    // The card says what to run; this machine's settings say where the CLI is and what it always
    // gets. Every path to an adapter goes through here, so there is one place the two are joined.
    private LaunchConfig Config(Card card)
        => LaunchComposition.Compose(card.LaunchConfig, settings.Defaults(card.AgentType));

    // The card already working in this card's folder, if the guard is on and this card is not
    // exempt from it. `board.All` rather than a column query: the rule decides for itself what
    // holding a folder means, and asking it about the whole store keeps that decision in one place.
    private Card? Blocking(Card card)
        => WorkingDirConflict.Blocking(card, board.All, directories, settings.PreventConcurrentWorkingDir);

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
