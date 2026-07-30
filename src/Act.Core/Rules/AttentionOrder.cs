using Act.Core.Model;

namespace Act.Core.Rules;

// Your turn holds several reasons to be looked at, so the column needs an order the eye can trust —
// which used to be a thing two separate columns said by existing. The order is by cost of waiting:
// a live session parked on a prompt is burning a turn nobody is answering, a dead one is already
// stopped, and finished work is not waiting on anything at all.
public static class AttentionOrder
{
    public static int Rank(Badge? badge) => badge switch
    {
        Badge.NeedsPermission => 0,
        Badge.NeedsAnswer => 1,
        Badge.Error => 2,
        Badge.Killed => 3,
        Badge.ReadyForReview => 4,
        _ => 5,
    };
}
