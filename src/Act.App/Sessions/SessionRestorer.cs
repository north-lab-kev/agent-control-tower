using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// Startup's half of restoring: every mid-flight card gets its terminal back before anyone looks at
// the board. Without it a restart leaves the cards intact and every one of them behind an empty
// pane, which reads as "the work is gone" for work that is only unattended.
//
// Best effort, card by card — one agent whose binary moved or whose transcript expired must not
// stop the others coming back — and all cards at once: the restores are independent spawns, the
// board serializes its own writes, and a single slow binary (a cold antivirus scan, a network
// share) must not hold every later terminal and the queue runner behind it.
public sealed class SessionRestorer(
    BoardState board,
    SessionLauncher launcher,
    ILogger<SessionRestorer> log)
{
    public async Task RestoreAllAsync(CancellationToken cancellationToken = default)
    {
        var restorable = board.RestorableUnattended.ToList();

        log.LogInformation("Restoring {Count} unattended terminal(s).", restorable.Count);

        await Task.WhenAll(restorable.Select(card => RestoreOneAsync(card, cancellationToken)));
    }

    private async Task RestoreOneAsync(Card card, CancellationToken cancellationToken)
    {
        using var scope = log.BeginTaskScope(card.Number, card.SessionId);

        try
        {
            var result = await launcher.RestoreAsync(card, TerminalSize.Default, cancellationToken);

            if (result.Message is { } refused)
                log.LogWarning("Terminal not restored — {Reason}", refused);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Restoring the terminal failed.");
        }
    }
}
