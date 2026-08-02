using Act.App.Resources;
using Act.App.Sessions;
using Act.App.Settings;
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
    IHostApplicationLifetime lifetime)
{
    private const int TitleBarHeight = 49;

    private const int MinWindowWidth = 1000;

    private const int MinWindowHeight = 320;

    // Windows reads the toast's header from the Application User Model ID, and Electron's default
    // makes every notification announce itself as `electron.app.Electron`. It has to match the
    // installer's `appId`, which is what puts the same id on the Start-menu shortcut Windows
    // resolves the display name from — a mismatch there and the packaged build is no better off.
    private const string AppUserModelId = "com.northlabkev.act";

    private readonly Lock gate = new();

    private BrowserWindow? window;

    private Task? revealing;

    private bool trayShown;

    public async Task StartAsync()
    {
        if (OperatingSystem.IsWindows())
            Electron.App.SetAppUserModelId(AppUserModelId);

        Electron.Menu.SetApplicationMenu([]);

        settings.Changed += ApplyCloseBehaviour;

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
        var options = new BrowserWindowOptions
        {
            Show = false,
            Icon = IconPath,
            TitleBarStyle = TitleBarStyle.hidden,
            MinWidth = MinWindowWidth,
            MinHeight = MinWindowHeight,
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

        opened.OnReadyToShow += () => opened.Show();
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
        Electron.Tray.SetToolTip(Strings.Shell_FullName);

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
    }

    private void HideTray()
    {
        if (!trayShown)
            return;

        Electron.Tray.Destroy();

        trayShown = false;
    }

    private MenuItem[] TrayMenu() =>
    [
        new MenuItem { Label = Strings.Tray_Open, Click = () => _ = RevealAsync() },
        new MenuItem { Type = MenuType.separator },
        new MenuItem { Label = Strings.Tray_Exit, Click = () => _ = ConfirmExitAsync() },
    ];

    // A native message box rather than the app's own dialog: the window this would have to open in
    // is usually the one the user just closed, and reviving it to ask whether to quit is a strange
    // thing to do. It says what is lost, because from the tray there is nothing on screen to say it.
    private async Task ConfirmExitAsync()
    {
        var live = sessions.LiveCount;

        var options = new MessageBoxOptions(Strings.Tray_ExitConfirm)
        {
            Type = MessageBoxType.warning,
            Title = Strings.Tray_Exit,
            Detail = live == 0
                ? Strings.Tray_ExitDetail
                : Text.Format(Strings.Tray_ExitDetailRunning, live),
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

        // Ended here rather than left to the host's disposal, because the confirmation promised it:
        // an agent must not outlive the app that was supervising it.
        await sessions.DisposeAsync();

        Electron.App.Exit(0);

        lifetime.StopApplication();
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "icon.ico");
}
