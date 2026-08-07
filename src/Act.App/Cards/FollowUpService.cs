using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Spawning;
using Act.Infrastructure.Logging;

namespace Act.App.Cards;

// The single link routine the spec's lineage consistency rule demands: minting the child, writing
// `parentId` and `children[]` in one operation, stamping the parent's timeline and persisting both.
// Nothing else creates a spawned card, so the two copies of the lineage cannot drift.
//
// It is also the only place the spawn budget is spent, which is why the quota check lives inside the
// gate rather than at the caller: two tool calls arriving together must not both see the last slot.
public sealed class FollowUpService(
    BoardState board,
    IAgentCapabilityCatalog catalog,
    UserSettingsService settings,
    IClock clock,
    ILogger<FollowUpService> log)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<FollowUpOutcome> CreateAsync(
        Guid parentId,
        FollowUpRequest request,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            return await SpawnAsync(parentId, request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FollowUpOutcome> SpawnAsync(
        Guid parentId,
        FollowUpRequest request,
        CancellationToken cancellationToken)
    {
        if (board.Card(parentId) is not { } parent)
            return FollowUpOutcome.Refused("The calling task no longer exists.");

        // Checked before the quota and before resolution, so a retried call is answered with the card
        // it already made rather than with a rejection or a duplicate.
        if (Existing(parent, request.ClientKey) is { } already)
            return FollowUpOutcome.Ok(Describe(already, [], alreadyExisted: true));

        var cap = settings.MaxFollowUpsPerCard;

        if (SpawnQuota.Exhausted(parent, cap))
            return FollowUpOutcome.Refused(
                $"Task #{parent.Number} has created its limit of {cap} follow-ups. Ask the user to "
                    + "raise the limit in ACT's settings, or put the remaining work in this task.");

        var resolution = FollowUpResolver.Resolve(parent, request, catalog, clock.Now);

        if (!resolution.CanCreate)
            return FollowUpOutcome.Refused([.. resolution.Rejections]);

        var child = resolution.Card!;

        child.SpawnKey = string.IsNullOrWhiteSpace(request.ClientKey) ? null : request.ClientKey.Trim();

        await board.CreateAsync(child, cancellationToken);

        parent.Children.Add(child.Id);
        parent.Transitions.Add(new Transition
        {
            At = clock.Now,
            Reason = TransitionReason.SpawnedFollowUp,
            Note = $"#{child.Number} · {child.Title}",
        });

        await board.UpdateAsync(parent, cancellationToken);

        using (log.BeginTaskScope(task: parent.Number))
            log.LogInformation(
                "Spawned follow-up {ChildNumber} on {Agent}.",
                child.Number,
                child.AgentType);

        return FollowUpOutcome.Ok(Describe(child, resolution.Adjustments, alreadyExisted: false));
    }

    // Board-wide by decision, and on-board only: an agent checking whether it already asked for
    // something is asking about live work, and a match in the archive would send it looking for a
    // card the user has put away.
    public TaskListing List(Guid callerId, string? column)
    {
        var wanted = Column(column);

        var visible = board.All
            .Where(card => card.IsOnBoard)
            .Where(card => wanted is null || card.Column == wanted)
            .OrderByDescending(card => card.Number)
            .Select(card => TaskSummary.From(card, callerId))
            .ToList();

        return TaskListing.Of(visible);
    }

    public TaskDetail? Detail(Guid callerId, string id)
        => Guid.TryParse(id, out var parsed) && board.Card(parsed) is { IsOnBoard: true } card
            ? TaskDetail.From(card, callerId)
            : null;

    private Card? Existing(Card parent, string? clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
            return null;

        var key = clientKey.Trim();

        return board.ChildrenOf(parent)
            .FirstOrDefault(child => string.Equals(child.SpawnKey, key, StringComparison.Ordinal));
    }

    private static FollowUpCreated Describe(
        Card child,
        IReadOnlyList<LaunchConfigAdjustment> adjustments,
        bool alreadyExisted) => new(
            Id: child.Id.ToString(),
            Number: child.Number,
            Agent: TaskWords.Of(child.AgentType),
            Model: child.LaunchConfig.Model,
            Effort: child.LaunchConfig.Effort,
            Permission: TaskWords.Of(child.LaunchConfig.PermissionMode),
            Schedule: TaskWords.Of(child.Schedule ?? TaskSchedule.Manual),
            Adjustments: [.. adjustments.Select(adjustment => adjustment.Reason)],
            AlreadyExisted: alreadyExisted);

    private static BoardColumn? Column(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "preparing" => BoardColumn.Preparing,
        "ready" => BoardColumn.Ready,
        "executing" => BoardColumn.Executing,
        "your_turn" or "yourturn" => BoardColumn.YourTurn,
        "completed" => BoardColumn.Completed,
        _ => null,
    };
}
