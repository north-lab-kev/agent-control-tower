using Act.App.Cards;
using Act.Core.Abstractions;

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
        foreach (var card in board.Resumable)
        {
            try
            {
                var result = await launcher.RestoreAsync(card, TerminalSize.Default, cancellationToken);

                if (result.Message is { } refused)
                    log.LogWarning("Task {Number}: terminal not restored — {Reason}", card.Number, refused);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                log.LogError(error, "Task {Number}: restoring the terminal failed.", card.Number);
            }
        }
    }
}
