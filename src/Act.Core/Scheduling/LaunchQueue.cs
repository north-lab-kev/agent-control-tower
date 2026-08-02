using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.Core.Scheduling;

public sealed record QueueEvaluation(
    IReadOnlyList<Card> Launch,
    IReadOnlyDictionary<Guid, ReadyHold> Holds,
    IReadOnlyDictionary<Guid, DateTimeOffset> Arm);

// The whole queue decision, as one pure function over the board.
//
// **One evaluation answers both questions.** The runner wants the ordered list to launch; the board
// wants a chip per Ready card saying why it has not. Deriving them separately is how a board ends
// up saying `queued` beside a card that started thirty seconds ago, so they come out of the same
// pass — and the launch list is exactly the cards for which no hold was found.
//
// **FIFO, by the instant a card became due and then by its number.** Nothing here weighs one task
// against another: a queue that reorders itself is a queue nobody can predict, and the number is
// the only tiebreak that is stable across restarts.
public static class LaunchQueue
{
    public static QueueEvaluation Evaluate(
        IReadOnlyList<Card> cards,
        QueuePolicy policy,
        Func<AgentType, AgentUsage?> usage,
        IWorkingDirectories directories,
        DateTimeOffset now)
    {
        var launch = new List<Card>();
        var holds = new Dictionary<Guid, ReadyHold>();
        var arm = new Dictionary<Guid, DateTimeOffset>();

        var used = ConcurrencySlots.Used(cards);
        var cap = ConcurrencySlots.Clamp(policy.MaxConcurrent);

        // The projection the launches themselves change: each one takes a slot and its folder, and
        // the next card in the queue has to be judged against the board as it will then be.
        var claimed = new List<Card>();

        foreach (var card in Queue(cards, usage, arm, now))
        {
            if (Hold(card, cards, claimed, policy, usage, directories, used, cap, now) is { } hold)
            {
                holds[card.Id] = hold;

                continue;
            }

            launch.Add(card);
            claimed.Add(card);
            used++;
        }

        return new QueueEvaluation(launch, holds, arm);
    }

    // Every Ready card, armed if it needs arming, ordered the way the runner will take them. Cards
    // with no due instant sort first: `now` means now, and a card waiting for a boundary should not
    // jump ahead of one that has been waiting since it was created.
    private static IEnumerable<Card> Queue(
        IReadOnlyList<Card> cards,
        Func<AgentType, AgentUsage?> usage,
        Dictionary<Guid, DateTimeOffset> arm,
        DateTimeOffset now)
    {
        var ready = new List<Card>();

        foreach (var card in cards.Where(card => card.IsOnBoard && card.Column is BoardColumn.Ready))
        {
            if (ScheduleArming.NeedsArming(card)
                && ScheduleArming.Arm(card, usage(card.AgentType)?.Of(UsageWindowKind.Session)) is { } at)
                arm[card.Id] = at;

            ready.Add(card);
        }

        return ready
            .Where(card => ScheduleArming.IsDue(card, now))
            .OrderBy(card => ScheduleArming.DueAt(card) ?? DateTimeOffset.MinValue)
            .ThenBy(card => card.Number);
    }

    // Null means launch it. The order of the checks is the precedence the chip renders by.
    private static ReadyHold? Hold(
        Card card,
        IReadOnlyList<Card> cards,
        IReadOnlyList<Card> claimed,
        QueuePolicy policy,
        Func<AgentType, AgentUsage?> usage,
        IWorkingDirectories directories,
        int used,
        int cap,
        DateTimeOffset now)
    {
        if (policy.AutoExecutionPaused)
            return new ReadyHold(LaunchHold.Paused);

        if (!policy.EnabledAgents.Contains(card.AgentType))
            return new ReadyHold(LaunchHold.AgentDisabled);

        if (UsageBackpressure.Blocking(usage(card.AgentType), now) is { } window)
            return new ReadyHold(LaunchHold.UsageLimit, Until: window.ResetsAt);

        // Against the cards already picked this pass as well as the board, since each of those is
        // about to take its folder — two Ready cards in one directory must not both be launched.
        var folder = WorkingDirConflict.Blocking(card, cards, directories, policy.PreventConcurrentWorkingDir)
            ?? Claiming(card, claimed, directories, policy.PreventConcurrentWorkingDir);

        if (folder is not null)
            return new ReadyHold(LaunchHold.WorkingDir, Blocker: folder);

        if (DependencyGate.Blocking(card, cards) is { } prerequisite)
            return new ReadyHold(LaunchHold.Dependency, Blocker: prerequisite);

        return used >= cap ? new ReadyHold(LaunchHold.Slot, Used: used, Cap: cap) : null;
    }

    // `WorkingDirConflict.Blocking` reads "is anyone working here", and a card this pass has only
    // decided to launch is still sitting in Ready — so it would answer no about every one of them.
    // The folder comparison is the same; only the column rule does not apply yet.
    private static Card? Claiming(
        Card card,
        IReadOnlyList<Card> claimed,
        IWorkingDirectories directories,
        bool enforced)
        => !enforced || card.AllowConcurrentWorkingDir
            ? null
            : claimed.FirstOrDefault(other => WorkingDirConflict.SameFolder(card, other, directories));
}
