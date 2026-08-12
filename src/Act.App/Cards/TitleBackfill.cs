using Act.Core.Model;

namespace Act.App.Cards;

public sealed class TitleBackfill(TaskTitles titles, BoardState board, ILogger<TitleBackfill> log)
{
    private readonly Dictionary<Guid, Run> runs = [];

    private readonly object gate = new();

    public event Action? Changed;

    public bool IsPending(Guid cardId)
    {
        lock (gate)
            return runs.ContainsKey(cardId);
    }

    public Task Idle
    {
        get
        {
            lock (gate)
                return Task.WhenAll(runs.Values.Select(run => run.Done.Task));
        }
    }

    public void Start(Card saved)
    {
        if (string.IsNullOrWhiteSpace(saved.InitialPrompt))
            return;

        var run = new Run();
        Run? previous;

        lock (gate)
        {
            runs.Remove(saved.Id, out previous);
            runs[saved.Id] = run;
        }

        previous?.Cancellation.Cancel();

        _ = BackfillAsync(saved.Id, saved.Title, saved.InitialPrompt, saved.AgentType, run);

        Changed?.Invoke();
    }

    public void Cancel(Guid cardId)
    {
        Run? run;

        lock (gate)
            runs.Remove(cardId, out run);

        if (run is null)
            return;

        run.Cancellation.Cancel();

        Changed?.Invoke();
    }

    private async Task BackfillAsync(Guid id, string placeholder, string prompt, AgentType agent, Run run)
    {
        try
        {
            var result = await titles.SuggestAsync(prompt, agent, run.Cancellation.Token);

            if (result is { Generated: true } suggestion && suggestion.Title != placeholder)
                await board.UpdateManyAsync(
                    [id],
                    card =>
                    {
                        if (card.Title == placeholder)
                            card.Title = suggestion.Title;
                    },
                    run.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogWarning(error, "Backfilling the generated title for card {CardId} failed.", id);
        }
        finally
        {
            Finish(id, run);
        }
    }

    private void Finish(Guid id, Run run)
    {
        bool owned;

        lock (gate)
            owned = runs.TryGetValue(id, out var current) && current == run && runs.Remove(id);

        if (owned)
            run.Cancellation.Dispose();

        Changed?.Invoke();

        run.Done.TrySetResult();
    }

    private sealed class Run
    {
        public CancellationTokenSource Cancellation { get; } = new();

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
