using Act.App.Cards;
using Act.App.Resources;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Act.App.Desktop;

// The desktop window, the tray icon, and the one rule that binds them: with close-to-tray on,
// closing the window does not end ACT.
//
// The window is genuinely destroyed rather than hidden, because Electron.NET's `close` handler
// never calls `preventDefault` — there is no way to cancel a close from C#. Coming back builds a
// new window, which costs a page load and nothing else: sessions belong to the registry, not to a
// view, so the agents keep working across the gap and the terminal re-attaches on the way back.
public sealed class DesktopShell(
    UserSettingsService settings,
    SessionRegistry sessions,
    BoardState board,
    IUpdater updater,
    IHostApplicationLifetime lifetime)
{
    private const int TitleBarHeight = 49;

    private const int MinWindowWidth = 1000;

    private const int MinWindowHeight = 320;

    private const int DefaultWindowWidth = 1440;

    private const int DefaultWindowHeight = 900;

    private static readonly TimeSpan BoundsSaveDelay = TimeSpan.FromMilliseconds(600);

    // Windows reads the toast's header from the Application User Model ID, and Electron's default
    // makes every notification announce itself as `electron.app.Electron`. It has to match the
    // installer's `appId`, which is what puts the same id on the Start-menu shortcut Windows
    // resolves the display name from — a mismatch there and the packaged build is no better off.
    private const string AppUserModelId = "com.northlabkev.act";

    private readonly Lock gate = new();

    private readonly bool remembered = settings.Window is not null;

    private readonly WindowBounds bounds = settings.Window ?? new WindowBounds
    {
        Width = DefaultWindowWidth,
        Height = DefaultWindowHeight,
    };

    private BrowserWindow? window;

    private Task? revealing;

    private Timer? boundsSave;

    private bool trayShown;

    private string tooltip = string.Empty;

    public async Task StartAsync()
    {
        if (OperatingSystem.IsWindows())
            Electron.App.SetAppUserModelId(AppUserModelId);

        Electron.Menu.SetApplicationMenu([]);

        settings.Changed += ApplyCloseBehaviour;
        board.Changed += RefreshTooltip;

        ApplyCloseBehaviour();

        await OpenWindowAsync();

        await Electron.App.RequestSingleInstanceLockAsync(
            (_, _) => _ = RevealAsync(),
            CancellationToken.None);
    }

    // Electron quits by itself when the last window closes, and that is the behaviour to keep when
    // the setting is off — the C#-initiated path is the exception, not the rule.
    private void ApplyCloseBehaviour()
    {
        var toTray = settings.CloseToTray;

        Electron.WindowManager.IsQuitOnWindowAllClosed = !toTray;

        if (toTray)
            ShowTray();
        else
            HideTray();
    }

    private async Task OpenWindowAsync()
    {
        // Every launch, not just a remembered one: the default size is a guess about a screen that
        // may be smaller than the guess.
        var placed = await PlaceAsync(bounds);

        var options = new BrowserWindowOptions
        {
            Show = false,
            Icon = IconPath,
            TitleBarStyle = TitleBarStyle.hidden,
            MinWidth = MinWindowWidth,
            MinHeight = MinWindowHeight,
            UseContentSize = true,
            Center = !remembered,
            Width = placed.Width,
            Height = placed.Height,
        };

        // Transparent on purpose. The overlay is native and can only be coloured at window
        // creation, so any fixed colour is wrong half the time — it cannot follow the theme, and
        // it painted a flat block over the end of a bar that is a gradient with a rule under it.
        // Left transparent, the page paints the whole strip and the seam disappears; only the
        // glyphs stay native, in a grey chosen to read on both themes.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            options.TitleBarOverlay = new TitleBarOverlay
            {
                Color = "rgba(0, 0, 0, 0)",
                SymbolColor = "#7c8b99",
                Height = TitleBarHeight,
            };

        var opened = await Electron.WindowManager.CreateWindowAsync(options);

        // Content bounds rather than window bounds, both here and when they are read back. What
        // Windows calls the window includes an invisible resize border that Electron reports but
        // does not accept, so a saved-then-restored window rectangle grows by it on every launch.
        if (remembered)
            opened.SetContentBounds(new Rectangle
            {
                X = placed.X,
                Y = placed.Y,
                Width = placed.Width,
                Height = placed.Height,
            });

        boundsSave ??= new Timer(_ => _ = CaptureBoundsAsync(), null, Timeout.Infinite, Timeout.Infinite);

        opened.OnResize += TrackBounds;
        opened.OnMove += TrackBounds;
        opened.OnMaximize += TrackBounds;
        opened.OnUnmaximize += TrackBounds;

        opened.OnReadyToShow += () =>
        {
            if (bounds.Maximized)
                opened.Maximize();

            opened.Show();
        };

        opened.OnClosed += () =>
        {
            lock (gate)
            {
                if (ReferenceEquals(window, opened))
                    window = null;
            }
        };

        lock (gate)
            window = opened;
    }

    private void TrackBounds() => boundsSave?.Change(BoundsSaveDelay, Timeout.InfiniteTimeSpan);

    // Only a window that is neither maximized nor minimized has bounds worth keeping — those states
    // are their own answer, and writing the screen-sized rectangle they report would lose the size
    // the user actually chose.
    private async Task CaptureBoundsAsync()
    {
        BrowserWindow? live;

        lock (gate)
            live = window;

        if (live is null)
            return;

        try
        {
            var maximized = await live.IsMaximizedAsync();

            if (!maximized && !await live.IsMinimizedAsync())
            {
                var rectangle = await live.GetContentBoundsAsync();

                bounds.X = rectangle.X;
                bounds.Y = rectangle.Y;
                bounds.Width = rectangle.Width;
                bounds.Height = rectangle.Height;
            }

            bounds.Maximized = maximized;

            settings.SetWindow(bounds);
        }
        catch (Exception)
        {
        }
    }

    private async Task<WindowBounds> PlaceAsync(WindowBounds wanted)
    {
        try
        {
            var displays = await Electron.Screen.GetAllDisplaysAsync();

            var screens = displays
                .Select(display => new ScreenArea(
                    display.WorkArea.X,
                    display.WorkArea.Y,
                    display.WorkArea.Width,
                    display.WorkArea.Height))
                .ToArray();

            return WindowPlacement.Fit(wanted, screens, MinWindowWidth, MinWindowHeight);
        }
        catch (Exception)
        {
            return wanted.Copy();
        }
    }

    private void OnTrayActivated(TrayClickEventArgs args, Rectangle bounds) => _ = RevealAsync();

    // Windows delivers a double click as a click *and* a double click, so two reveals race — and
    // two reveals with no window would build two windows. Whoever arrives second waits on the
    // first instead of starting its own.
    public Task RevealAsync()
    {
        lock (gate)
        {
            if (revealing is { IsCompleted: false } inFlight)
                return inFlight;

            return revealing = RevealCoreAsync();
        }
    }

    private async Task RevealCoreAsync()
    {
        BrowserWindow? existing;

        lock (gate)
            existing = window;

        if (existing is null)
        {
            await OpenWindowAsync();

            return;
        }

        existing.Restore();
        existing.Show();
        existing.Focus();
    }

    private void ShowTray()
    {
        if (trayShown)
            return;

        Electron.Tray.Show(IconPath, TrayMenu());

        // Subscribed here, after the icon exists, because Electron.NET drops a registration made
        // before it: every handler in its tray bridge is guarded by `if (tray.value)`. The pair of
        // detach-then-attach keeps one handler across a tray that is destroyed and rebuilt when
        // the setting is toggled off and on again.
        Electron.Tray.OnClick -= OnTrayActivated;
        Electron.Tray.OnClick += OnTrayActivated;

        // Double click is where Windows and macOS users reach for first, and it is theirs alone —
        // Linux has no such event, and the single click above already covers it there.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Electron.Tray.OnDoubleClick -= OnTrayActivated;
            Electron.Tray.OnDoubleClick += OnTrayActivated;
        }

        trayShown = true;

        RefreshTooltip();
    }

    private void HideTray()
    {
        if (!trayShown)
            return;

        Electron.Tray.Destroy();

        trayShown = false;

        // Forgotten with the icon, so the tray rebuilt by toggling the setting back on is told its
        // text again rather than being left with the name because the count has not moved since.
        lock (gate)
            tooltip = string.Empty;
    }

    // Called on every board write, which is far more often than the count changes — and every call
    // that gets past the cache is a socket round trip to redraw a string nobody is hovering over.
    private void RefreshTooltip()
    {
        if (!trayShown)
            return;

        var text = TrayTooltip.For(board.All);

        lock (gate)
        {
            if (text == tooltip)
                return;

            tooltip = text;
        }

        Electron.Tray.SetToolTip(text);
    }

    private MenuItem[] TrayMenu() =>
    [
        new MenuItem { Label = Strings.Tray_Open, Click = () => _ = RevealAsync() },
        new MenuItem { Type = MenuType.separator },
        new MenuItem { Label = Strings.Tray_Exit, Click = () => _ = ConfirmExitAsync() },
    ];

    // A native message box rather than the app's own dialog: the window this would have to open in
    // is usually the one the user just closed, and reviving it to ask whether to quit is a strange
    // thing to do. It says what is lost, because from the tray there is nothing on screen to say it
    // — and when the board says nothing is lost, it asks nothing and just exits.
    private async Task ConfirmExitAsync()
    {
        var stakes = ExitPolicy.Assess(board.All, settings.AutoExecutionPaused);

        if (stakes.Warning is ExitWarning.None)
        {
            await ExitAsync();

            return;
        }

        var options = new MessageBoxOptions(Strings.Tray_ExitConfirm)
        {
            Type = MessageBoxType.warning,
            Title = Strings.Tray_Exit,
            Detail = Detail(stakes, updater.IsReady),
            Buttons = [Strings.Tray_ExitYes, Strings.NewTask_Cancel],
            DefaultId = 1,
            CancelId = 1,
        };

        // Otherwise Windows renders the first button as a command link, which reads like a
        // suggestion — the wrong tone for the one button that stops running work.
        if (OperatingSystem.IsWindows())
            options.NoLink = true;

        BrowserWindow? parent;

        lock (gate)
            parent = window;

        // A parentless message box shows nothing at all in Electron, and from the tray there is
        // usually no parent — so the window comes back to carry the question. Being shown what is
        // about to be stopped before agreeing to stop it is no bad thing.
        if (parent is null)
        {
            await RevealAsync();

            lock (gate)
                parent = window;
        }

        if (parent is null)
            return;

        var result = await Electron.Dialog.ShowMessageBoxAsync(parent, options);

        if (result.Response != 0)
            return;

        await ExitAsync();
    }

    // The update line is appended rather than folded into the wordings, because it is a second,
    // independent fact about this exit: what is lost by leaving is one thing, and what leaving will
    // additionally do is another. Said here and not when nothing is at stake, where the exit is
    // silent and the toast has already promised exactly this.
    private static string Detail(ExitStakes stakes, bool updateReady)
    {
        var detail = stakes.Warning switch
        {
            ExitWarning.Running => Text.Format(
                Text.Plural(stakes.Running, Strings.Tray_ExitDetailRunning_One, Strings.Tray_ExitDetailRunning_Many),
                stakes.Running),
            ExitWarning.RunningAndScheduled => Text.Format(
                Text.Plural(stakes.Running, Strings.Tray_ExitDetailBoth_One, Strings.Tray_ExitDetailBoth_Many),
                stakes.Running),
            _ => Strings.Tray_ExitDetailScheduled,
        };

        return updateReady ? $"{detail}\n\n{Strings.Tray_ExitDetailUpdate}" : detail;
    }

    // Ended here rather than left to the host's disposal, because the confirmation promised it:
    // an agent must not outlive the app that was supervising it.
    private async Task ExitAsync()
    {
        await sessions.DisposeAsync();

        // The one exit that is not `Exit(0)`. `AutoInstallOnAppQuit` would not cover this path —
        // `app.exit()` skips the quit handling it hangs off — so a downloaded update would sit on
        // disk forever for anyone who leaves through the tray. `BaseUpdater.install` ignores a
        // second caller, so the two routes cannot both fire.
        if (updater.IsReady)
            updater.InstallAndExit();
        else
            Electron.App.Exit(0);

        lifetime.StopApplication();
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "icon.ico");
}
