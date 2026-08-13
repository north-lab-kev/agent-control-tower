using Act.App.Sessions;
using Act.Core.Model;

namespace Act.App.Cards;

// **Kills first, then marks deleted**, and in one place because the order is the load-bearing part:
// deleting the card while its process ran would leave an agent working in a directory with nothing on
// the board pointing at it, which is the one state ACT exists to prevent. Deleting is reversible and
// the process it was driving is not, so the session ends either way and a restored card starts from
// Ready rather than mid-turn.
//
// Shared by the task page and the board, which both delete cards. A second copy of this ordering is
// how one of the two eventually loses it.
public sealed class CardDeletion(BoardState board, SessionRegistry registry)
{
    public async Task DeleteAsync(
        Card card,
        bool includeChildren,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in includeChildren ? board.ChildrenOf(card).Append(card) : [card])
            await registry.EndAsync(target.Id);

        await board.DeleteAsync(card, includeChildren, cancellationToken);
    }
}
