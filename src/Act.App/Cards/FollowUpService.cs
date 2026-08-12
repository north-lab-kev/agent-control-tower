using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Spawning;
using Act.Infrastructure.Logging;

namespace Act.App.Cards;

// The policy half of a spawn — the budget, the idempotency key, the refusals — over the single link
// routine `BoardState.LinkAsync` owns. Nothing else creates a spawned card, so the two copies of the
// lineage cannot drift.
//
// It is also the only place the spawn budget is spent, which is why the quota check lives inside the
// gate rather than at the caller: two tool calls arriving together must not both see the last slot.
public sealed class FollowUpService(
    BoardState board,
    IAgentCapabilityCatalog catalog,
    IWorkingDirectories directories,
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
        if (Existing(parent, request.NormalizedClientKey) is { } already)
            return FollowUpOutcome.Ok(Describe(already, [], alreadyExisted: true));

        var cap = settings.MaxFollowUpsPerCard;

        if (SpawnQuota.Exhausted(parent, cap))
            return FollowUpOutcome.Refused(
                $"Task #{parent.Number} has created its limit of {cap} follow-ups. Ask the user to "
                    + "raise the limit in ACT's settings, or put the remaining work in this task.");

        var resolution = FollowUpResolver.Resolve(parent, request, catalog, directories, board.All, clock.Now);

        if (!resolution.CanCreate)
            return FollowUpOutcome.Refused([.. resolution.Rejections]);

        var child = resolution.Card!;

        child.SpawnKey = request.NormalizedClientKey;

        await board.LinkAsync(parent.Id, child, cancellationToken);

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

    // On-board only, like the two reads: a match the user has deleted or archived would answer the
    // retry with an id `get_task` then denies, and the work would never reappear. The key arrives
    // already normalized — `FollowUpRequest.NormalizedClientKey` is the one spelling of that.
    private Card? Existing(Card parent, string? key)
    {
        if (key is null)
            return null;

        return board.ChildrenOf(parent)
            .Where(child => child.IsOnBoard)
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

    // Unknown words mean "no filter" rather than a refusal — pinned by test — so the parse only has
    // to be the same one the rest of the vocabulary uses.
    private static BoardColumn? Column(string? name)
        => TaskWords.TryParse(name, out BoardColumn column) ? column : null;
}
