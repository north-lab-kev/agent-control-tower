namespace Act.App.Updates;

public enum UpdateStage
{
    // Nothing has been asked yet, or the policy says never to ask.
    Idle,

    Checking,

    UpToDate,

    // A newer version exists and has not been fetched — either because the policy says not to, or
    // because fetching it is about to start.
    Available,

    Downloading,

    // On disk, and applied the next time ACT exits.
    Ready,

    // The check could not be completed. Deliberately not an error: no release published yet, no
    // network, a private repository. It says "ask again later" and nothing more.
    Unavailable,
}

// What the last pass concluded, rendered by the Settings page. `Version` is the version on offer,
// null unless one is; `Percent` is meaningful only while downloading.
public sealed record UpdateStatus(
    UpdateStage Stage,
    string? Version = null,
    int Percent = 0,
    DateTimeOffset? CheckedAt = null)
{
    public static readonly UpdateStatus Idle = new(UpdateStage.Idle);

    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading;
}
