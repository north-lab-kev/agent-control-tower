using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// Startup's half of restoring: every mid-flight card gets its terminal back before anyone looks at
// the board. Without it a restart leaves the cards intact and every one of them behind an empty
// pane, which reads as "the work is gone" for work that is only unattended.
//
// Best effort, card by card. One agent whose binary moved or whose transcript expired must not stop
// the others coming back, so a failure is recorded on that card and the loop carries on.
public sealed class SessionRestorer(
    BoardState board,
    SessionLauncher launcher,
    ILogger<SessionRestorer> log)
{
    public async Task RestoreAllAsync(CancellationToken cancellationToken = default)
    {
        var restorable = board.RestorableUnattended.ToList();

        log.LogInformation("Restoring {Count} unattended terminal(s).", restorable.Count);

        foreach (var card in restorable)
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
                return;
            }
            catch (Exception error)
            {
                log.LogError(error, "Restoring the terminal failed.");
            }
        }
    }
}
