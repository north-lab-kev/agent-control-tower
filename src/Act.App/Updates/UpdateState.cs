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

    // **A percentage may only ever refine a download that is still running.** `Progress<T>` does not
    // invoke its callback inline — it posts to a synchronization context or the thread pool — so a
    // report routinely lands *after* the download it describes has already finished, and a plain
    // `Publish` would then paint `Downloading 60%` over the `Ready` that followed it. That one does
    // not heal: `UpdatePump.PassAsync` stops looking once a version is ready to install, so the
    // settings page keeps promising a download that finished hours ago and the user is never told
    // the update is waiting for them. The same arrival after a failed download buries `Available`.
    //
    // Decided under the write's own lock rather than by the caller, because a check outside it is a
    // window: the reporting thread could pass the test and be preempted before it wrote.
    public void PublishProgress(string version, int percent)
    {
        lock (gate)
        {
            if (current.Stage is not UpdateStage.Downloading || current.Version != version)
                return;

            var moved = current with { Percent = percent };

            if (current == moved)
                return;

            current = moved;
        }

        Changed?.Invoke();
    }
}
