using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;

namespace Act.App.Components.Layout;

// The quota strip in the top bar. What it says is `UsageMeters`'; what is left here is the two
// subscriptions that keep it current and the tick that advances a countdown nothing pushes.
public partial class UsageIndicator(UsageState usage, UserSettingsService settings, IClock clock) : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource stopping = new();

    private IReadOnlyList<UsageMeter> Meters => UsageMeters.For(usage.Results, settings.EnabledAgents, clock.Now);

    private IReadOnlyList<UsageNotice> Notices => UsageMeters.Notices(usage.Results, settings.EnabledAgents);

    protected override void OnInitialized()
    {
        usage.Changed += OnChanged;

        // A meter for an agent you have switched off has to leave the bar the moment you switch it,
        // not whenever the next poll happens to land.
        settings.Changed += OnChanged;

        _ = TickAsync();
    }

    public void Dispose()
    {
        usage.Changed -= OnChanged;
        settings.Changed -= OnChanged;

        stopping.Cancel();
        stopping.Dispose();
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    // The reset countdown is derived from an instant, so nothing raises an event when it advances.
    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(Tick);

        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
                await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
