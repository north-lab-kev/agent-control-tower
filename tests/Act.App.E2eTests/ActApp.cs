using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.Storage;
using Act.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Act.App.E2eTests;

// The real app, on a real port, with fake agents. Everything else in this project is a browser pointed
// at whatever this starts.
//
// Two hosts, which looks wrong and is not: `WebApplicationFactory` insists on owning a `TestServer`,
// and a `TestServer` has no socket for a browser to connect to. So the builder is built twice — once
// for the in-memory host the base class demands, once for a Kestrel host on a real port Playwright can
// reach. Kestrel starts **first**, because whichever host starts last wins the static `IServer` slot.
//
// Everything a spec asks of the app goes through `App`, the *Kestrel* host's container. `Services` on
// the base class is the other one; reading the board from it would be reading a board nobody is
// looking at.
public sealed class ActApp : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "act-e2e", Guid.NewGuid().ToString("n"));

    private readonly Dictionary<string, string?> restore = [];

    private string dataDirectory => Path.Combine(root, "app");

    private string shadowDirectory => Path.Combine(root, "shadow");

    private IHost? kestrel;

    private string url = string.Empty;

    // Environment variables rather than `UseSetting`, because `Program.cs` reads the data directory out
    // of `builder.Configuration` on its second line — before any `ConfigureWebHost` callback has had a
    // chance to run. The environment is the only channel that is already there when it looks.
    //
    // Which is also why this assembly runs its tests one at a time: see `AssemblyInfo`.
    public ActApp()
    {
        Set(ActDataDirectory.OverrideKey, dataDirectory);
        Set("ASPNETCORE_ENVIRONMENT", "E2e");

        // One polls a vendor endpoint over the network; the other would report this run as if it were
        // somebody's install.
        Set("Usage__Enabled", "false");
        Set("Telemetry__Enabled", "false");
        Set("Electron__Enabled", "false");
    }

    // Written before the app boots, so the first render already shows them — a spec that needs a board
    // states it here rather than clicking one together.
    public List<Card> Cards { get; } = [];

    // Set before boot, for the same reason: the queue runs its first pass moments after startup, so a spec
    // that wants it held has to say so before there is anything to hold.
    public bool Paused { get; set; }

    public MockAgentAdapter Claude { get; } = new(AgentType.ClaudeCode);

    public MockAgentAdapter Codex { get; } = new(AgentType.Codex);

    // Reading this is what boots the app: `WebApplicationFactory` builds nothing until its container is
    // asked for, and the address only exists once Kestrel has bound a port.
    public string Url
    {
        get
        {
            _ = Services;

            return url;
        }
    }

    public IServiceProvider App
    {
        get
        {
            _ = Services;

            return kestrel!.Services;
        }
    }

    public BoardState Board => App.GetRequiredService<BoardState>();

    public UserSettingsService Settings => App.GetRequiredService<UserSettingsService>();

    public SessionRegistry Registry => App.GetRequiredService<SessionRegistry>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Without this every asset 500s. `MapStaticAssets` resolves files through the web-root file
        // provider, and the web root here is the *test* project's folder, which has no `wwwroot` — the
        // manifest that points back at the real files is only loaded automatically in Development, and
        // this host deliberately runs as `E2e`. So load it explicitly.
        builder.UseStaticWebAssets();

        // Every provider, including the Windows event log one the host adds by default. It is not about
        // noise: `StartupLog` watches for unobserved task exceptions and logs them, and on teardown that
        // handler runs on the finalizer thread *after* the providers are disposed — the event log one
        // then throws `ObjectDisposedException` out of a finalizer, which takes the whole test host
        // process down. With no providers the log call is a no-op and teardown is quiet.
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentAdapter>();
            services.AddSingleton<IAgentAdapter>(Claude);
            services.AddSingleton<IAgentAdapter>(Codex);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(shadowDirectory);

        // The base class casts whatever this returns to a `TestServer`, so a second host has to exist
        // whether or not anything talks to it — and it boots the whole app, LiteDB included. LiteDB
        // takes an exclusive lock on `act.db`, so the two hosts cannot share a data directory: the
        // second one to open it dies with "used by another process". Hence a shadow store nobody reads.
        //
        // The environment is re-read on each `Build()`, because the resolver re-runs `Program` from the
        // top every time — which is what makes setting it between the two builds work at all.
        Environment.SetEnvironmentVariable(ActDataDirectory.OverrideKey, shadowDirectory);

        var testHost = builder.Build();

        Environment.SetEnvironmentVariable(ActDataDirectory.OverrideKey, dataDirectory);

        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));

        kestrel = builder.Build();
        kestrel.Start();

        url = kestrel.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        testHost.Start();

        Seed();

        return testHost;
    }

    // Straight off `kestrel`, never through `Board`: this runs *inside* `CreateHost`, and `Board` goes
    // through `Services`, which would re-enter the host builder that is still running.
    private void Seed()
    {
        if (Paused)
            kestrel!.Services.GetRequiredService<UserSettingsService>().SetAutoExecutionPaused(true);

        var board = kestrel!.Services.GetRequiredService<BoardState>();

        foreach (var card in Cards)
            board.CreateAsync(card).GetAwaiter().GetResult();
    }

    private void Set(string name, string value)
    {
        restore[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable(name, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        kestrel?.StopAsync().GetAwaiter().GetResult();
        kestrel?.Dispose();

        foreach (var (name, value) in restore)
            Environment.SetEnvironmentVariable(name, value);

        // Best effort: a LiteDB file the OS is still holding must not fail the suite.
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
