using Act.App.Desktop;

namespace Act.App.UiTests;

// Browser mode by default, because that is the registration `AddActApp` makes and the one every page
// has to work under. `Refusal` is the shape that matters most: the real bridge reports a path the OS
// would not open by *returning* a message rather than throwing, and a page that treats null as the
// only success is what this catches.
internal sealed class FakeDesktopBridge : IDesktopBridge
{
    public bool IsDesktop { get; set; }

    public List<string> Opened { get; } = [];

    public List<string> External { get; } = [];

    public string? Refusal { get; set; }

    public Exception? Fails { get; set; }

    public int Restarts { get; private set; }

    public Task OpenExternalAsync(string url)
    {
        External.Add(url);

        return Task.CompletedTask;
    }

    public Task RestartForUpdateAsync()
    {
        Restarts++;

        return Task.CompletedTask;
    }

    public Task<string?> OpenPathAsync(string path)
    {
        if (Fails is { } failure)
            return Task.FromException<string?>(failure);

        Opened.Add(path);

        return Task.FromResult(Refusal);
    }
}
