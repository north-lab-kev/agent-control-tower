namespace Act.App.Updates;

// Where the Settings page reads the updater's answer from, and the only thing the pump writes to.
// Modelled on `UsageState`: a value, an event, and a publish that stays quiet when nothing moved —
// a download reports progress several times a second, and re-rendering the page for a percentage
// that has not changed is the version of this that shows up as a busy CPU.
public sealed class UpdateState
{
    private readonly Lock gate = new();

    private UpdateStatus current = UpdateStatus.Idle;

    public event Action? Changed;

    public UpdateStatus Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    public void Publish(UpdateStatus status)
    {
        lock (gate)
        {
            if (current == status)
                return;

            current = status;
        }

        Changed?.Invoke();
    }
}
