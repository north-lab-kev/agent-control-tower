using Act.App.Hosting;
using Act.App.Settings;

namespace Act.App.Cards;

public sealed class RetentionPump(
    BoardState board,
    UserSettingsService settings,
    ILogger<RetentionPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly BackgroundWork work = new(log);

    public void Start()
    {
        settings.Changed += OnSettingsChanged;

        work.StartLoop("Completed-card retention", Interval, _ => work.RunAsync(SweepAsync));
    }

    // Through the same gate as the hourly sweep, so changing the window while one is running does
    // not start a second pass over the same cards.
    private void OnSettingsChanged() => work.Request("A retention sweep", SweepAsync);

    private Task SweepAsync(CancellationToken cancellationToken)
        => board.ApplyRetentionAsync(settings.CompletedRetentionWindow, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;

        await work.DisposeAsync();
    }
}
