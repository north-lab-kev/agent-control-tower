using System.Globalization;
using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Notifications;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Updates;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.FileSystem;
using Act.Infrastructure.Logging;
using Act.TestSupport;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;

namespace Act.App.UiTests;

// The app's own service graph over the fakes the rest of this project already uses, so a component
// test is a `Render<T>` and nothing else. Everything is registered here rather than per suite for one
// reason: a page reaches for services its markup never mentions — a Radzen `DialogService`, an
// `IClock` two components down — and a graph assembled per test is a graph that is subtly different
// per test.
//
// Deliberately *not* `AddActApp`: that wants an `IConfiguration`, a data directory and a web host, and
// it would register the real store, the real filesystem and the real Electron branch. What is shared
// with production is the wiring shape, not the composition root.
//
// The clock is frozen. A strip recomputes its face on every render, so a clock that advanced per read
// would move the numbers between two assertions in the same test.
public abstract class ComponentTest : BunitContext, IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    protected ComponentTest()
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");

        Claude = new MockAgentAdapter(AgentType.ClaudeCode, clock: Clock);
        Codex = new MockAgentAdapter(AgentType.Codex, NarrowerCapabilities, Clock);

        // What `AgentInstallDiscovery` leaves behind on a machine where both CLIs are on `PATH` — an
        // entry per adapter, enabled, with no recorded binary. Seeded into the *store* rather than by
        // running discovery, so nothing resolves a service here and a test is still free to register
        // one. It matters because `EnabledAgents` reads only stored entries: without this every page
        // that offers an agent offers none, which is a state the real app never reaches.
        SettingsStore.Save(new UserSettings
        {
            Agents =
            [
                new AgentDefaults { Agent = AgentType.ClaudeCode },
                new AgentDefaults { Agent = AgentType.Codex },
            ],
            AgentInstallsProbed = true,
        });

        Services.AddLogging();
        Services.AddRadzenComponents();

        Services.AddSingleton<IClock>(Clock);
        Services.AddSingleton<ICardStore>(Cards);
        Services.AddSingleton<IAttachmentStore>(Attachments);
        Services.AddSingleton<ISettingsStore>(SettingsStore);
        Services.AddSingleton<IWorkingDirectories>(Directories);
        Services.AddSingleton<IExecutableProbe>(Probe);
        Services.AddSingleton<IDesktopBridge>(Desktop);
        Services.AddSingleton<INotifier>(Notifier);
        Services.AddSingleton<IUpdater>(Updater);
        Services.AddSingleton<ITelemetrySink>(Telemetry);
        Services.AddSingleton<ISleepInhibitor>(Sleep);
        Services.AddSingleton<IHookEndpoint>(new StubHookEndpoint());
        Services.AddSingleton<IAgentConfigFiles>(new StubAgentConfigFiles());
        Services.AddSingleton<IAssetVersions>(new FakeAssetVersions());
        Services.AddSingleton(new ActLogLocation("/logs/act"));

        Services.AddSingleton<IAgentAdapter>(Claude);
        Services.AddSingleton<IAgentAdapter>(Codex);
        Services.AddSingleton<IAgentCapabilityCatalog, AgentCapabilityCatalog>();

        Services.AddSingleton<AppCulture>();
        Services.AddSingleton<UserSettingsService>();
        Services.AddSingleton<BoardState>();
        Services.AddSingleton<SessionRegistry>();
        Services.AddSingleton<UiPresence>();
        Services.AddSingleton<DeepLinkRouter>();
        Services.AddSingleton<NotificationDispatcher>();
        Services.AddSingleton<SessionLauncher>();
        Services.AddSingleton<UsageState>();

        // Registered but never started: the settings page injects both, and a pump that ran here
        // would put an update check behind every component test that happens to render it.
        Services.AddSingleton<UpdateState>();
        Services.AddSingleton<UpdatePump>();
        Services.AddSingleton<TerminalGeometry>();
        Services.AddSingleton<QueueRunner>();
        Services.AddSingleton<CardCompleter>();
        Services.AddSingleton<CardReopener>();
        Services.AddSingleton<FollowUpService>();
        Services.AddSingleton<TaskTitles>();
        Services.AddSingleton<TitleBackfill>();
        Services.AddSingleton<AttachmentOpener>();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // Subscribed here rather than in the constructor, because resolving anything freezes the service
    // collection — and before the test body, because a dialog opened by a click has to have been recorded
    // by the time the assertion asks.
    Task IAsyncLifetime.InitializeAsync()
    {
        Services.GetRequiredService<DialogService>().OnOpen +=
            (_, dialog, parameters, _) => opened.Add(new OpenedDialog(dialog, parameters));

        return Task.CompletedTask;
    }

    // Async on purpose: `SessionRegistry` and `QueueRunner` are `IAsyncDisposable` only, and the
    // container refuses to tear itself down synchronously while it owns one.

    async Task IAsyncLifetime.DisposeAsync() => await ((IAsyncDisposable)this).DisposeAsync();

    private readonly List<OpenedDialog> opened = [];

    internal FrozenClock Clock { get; } = new(Now);

    internal FakeCardStore Cards { get; } = new([]);

    internal FakeAttachmentStore Attachments { get; } = new();

    internal FakeSettingsStore SettingsStore { get; } = new();

    internal FakeWorkingDirectories Directories { get; } = new();

    internal FakeExecutableProbe Probe { get; } = new();

    internal FakeDesktopBridge Desktop { get; } = new();

    internal RecordingNotifier Notifier { get; } = new();

    internal FakeUpdater Updater { get; } = new();

    internal RecordingTelemetrySink Telemetry { get; } = new();

    internal FakeSleepInhibitor Sleep { get; } = new();

    internal MockAgentAdapter Claude { get; }

    internal MockAgentAdapter Codex { get; }

    // The second adapter is deliberately *narrower* than the first — one model, two permission modes, no
    // desktop handoff. Not a claim about the real Codex: it is what makes the form's cascades testable at
    // all. Two adapters with identical capabilities can never show that switching agent drops a model the
    // new one does not offer, which is the rule `OnAgentChanged` exists for.
    internal static AgentCapabilities NarrowerCapabilities { get; } = new(
        [new AgentModel(MockAgentAdapter.DeepModel, MockAgentAdapter.DeepModelName, ["low", "high", "max"], "high")],
        MockAgentAdapter.DeepModel,
        [PermissionMode.Default, PermissionMode.Plan]);

    internal BoardState Board => Services.GetRequiredService<BoardState>();

    internal UserSettingsService Settings => Services.GetRequiredService<UserSettingsService>();

    internal SessionRegistry Registry => Services.GetRequiredService<SessionRegistry>();

    internal SessionLauncher Launcher => Services.GetRequiredService<SessionLauncher>();

    internal QueueRunner Queue => Services.GetRequiredService<QueueRunner>();

    internal FollowUpService FollowUps => Services.GetRequiredService<FollowUpService>();

    internal TitleBackfill Backfill => Services.GetRequiredService<TitleBackfill>();

    internal UsageState Usage => Services.GetRequiredService<UsageState>();

    internal UpdateState Updates => Services.GetRequiredService<UpdateState>();

    internal BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    // Radzen's own host, which no page under test renders — the toasts live in `MainLayout`. So what a
    // page reports is read off the service rather than found in the markup.
    internal NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    // Straight into the store and then loaded, rather than through `BoardState.CreateAsync`: a test
    // describes a board that already exists, and going through the create path would restamp every
    // card's order on the way in.
    internal async Task<BoardState> BoardWith(params Card[] cards)
    {
        foreach (var card in cards)
            await Cards.AddAsync(card);

        await Board.LoadAsync();

        return Board;
    }

    // The url the page last asked for, relative to the base — what `NavigationManager.NavigateTo`
    // did, without a test having to strip `http://localhost/` off it every time.
    internal string Route => Navigation.ToBaseRelativePath(Navigation.Uri);

    // A dialog the way a page actually gets one: rendered inside Radzen's own host and awaited, so what
    // a button does travels the whole route the caller depends on — `Close` → the host → the task
    // `OpenAsync` handed back. Driving `DialogService.Close` with nothing open proves nothing, because
    // the service drops it.
    internal DialogRun<TDialog> OpenDialog<TDialog>(params (string Name, object? Value)[] parameters)
        where TDialog : ComponentBase
    {
        var host = Render<RadzenComponents>();

        var result = Services.GetRequiredService<DialogService>().OpenAsync<TDialog>(
            typeof(TDialog).Name,
            parameters.ToDictionary(entry => entry.Name, entry => entry.Value),
            new DialogOptions());

        return new DialogRun<TDialog>(host, result);
    }

    // A page under test renders no `RadzenComponents`, so a dialog it opens produces no markup here — only
    // an event. These two are how a test reads and answers one: what was opened, and then the answer, sent
    // on the renderer's dispatcher because the awaiting page resumes into `StateHasChanged`.
    internal IReadOnlyList<OpenedDialog> DialogsOpened => opened;

    internal Task AnswerDialog<T>(IRenderedComponent<T> cut, object? result)
        where T : ComponentBase
        => cut.InvokeAsync(() => Services.GetRequiredService<DialogService>().Close(result));

    internal sealed record OpenedDialog(Type Dialog, Dictionary<string, object?> Parameters);

    internal sealed record DialogRun<TDialog>(IRenderedComponent<RadzenComponents> Host, Task<dynamic?> Result)
        where TDialog : ComponentBase
    {
        // Whatever `Close` was given, or null for the X and the overlay — which every caller in ACT
        // reads as a cancel.
        internal async Task<object?> ClosedWith(string selector, int index = 0)
        {
            var buttons = Host.WaitForElements(selector);

            buttons[index].Click();

            return await Result;
        }
    }
}

internal sealed class FakeAssetVersions : IAssetVersions
{
    public string For(System.Reflection.Assembly assembly) => string.Empty;
}
