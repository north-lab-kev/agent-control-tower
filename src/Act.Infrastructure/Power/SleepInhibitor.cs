using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Act.Core.Abstractions;

namespace Act.Infrastructure.Power;

// Three mechanisms, because no two of these platforms agree. Windows takes a flag on a *thread*
// and drops it the moment that thread ends, so one parks here for the life of the hold. macOS and
// Linux both express it as a child process that must stay alive — `caffeinate` and
// `systemd-inhibit` — so the hold is that process and the release is its death.
//
// Every path degrades to doing nothing rather than throwing. A missing `systemd-inhibit` means a
// machine that may sleep, which is the problem the setting exists to solve; it is not a reason for
// the app not to start.
public sealed class SleepInhibitor : ISleepInhibitor, IDisposable
{
    private const uint Continuous = 0x80000000;

    private const uint SystemRequired = 0x00000001;

    private readonly Lock gate = new();

    private ManualResetEventSlim? parked;

    private Process? helper;

    public bool IsHeld { get; private set; }

    public void Hold()
    {
        lock (gate)
        {
            if (IsHeld)
                return;

            IsHeld = OperatingSystem.IsWindows() ? ParkThread() : StartHelper();
        }
    }

    public void Release()
    {
        lock (gate)
        {
            if (!IsHeld)
                return;

            IsHeld = false;

            // Set once and never again: the parked thread disposes the signal on its way out.
            parked?.Set();
            parked = null;

            StopHelper();
        }
    }

    public void Dispose() => Release();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);

    private bool ParkThread()
    {
        var signal = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            SetThreadExecutionState(Continuous | SystemRequired);

            signal.Wait();

            SetThreadExecutionState(Continuous);
            signal.Dispose();
        })
        {
            IsBackground = true,
            Name = "act-stay-awake",
        };

        thread.Start();

        parked = signal;

        return true;
    }

    private bool StartHelper()
    {
        // `caffeinate` with no command runs until it is killed, which is exactly the shape of a
        // hold. `systemd-inhibit` needs one, so it gets a sleep it will never finish.
        var start = OperatingSystem.IsMacOS()
            ? new ProcessStartInfo("caffeinate") { ArgumentList = { "-i", "-s" } }
            : new ProcessStartInfo("systemd-inhibit")
            {
                ArgumentList =
                {
                    "--what=idle:sleep",
                    "--who=ACT",
                    "--why=ACT is running agent sessions",
                    "--mode=block",
                    "sleep",
                    "infinity",
                },
            };

        start.UseShellExecute = false;
        start.CreateNoWindow = true;

        try
        {
            helper = Process.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or PlatformNotSupportedException)
        {
            helper = null;
        }

        return helper is not null;
    }

    private void StopHelper()
    {
        if (helper is not { } running)
            return;

        helper = null;

        try
        {
            if (!running.HasExited)
                running.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException
            or Win32Exception)
        {
        }

        running.Dispose();
    }
}
