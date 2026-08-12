using System.Net;
using System.Net.Sockets;

namespace Act.Infrastructure.Hooks;

// Sticky, not fresh each start. The port is chosen here rather than by handing Kestrel `0`
// because adapters need the url *before* the host starts listening — and because a port that
// moves between ACT restarts costs the user a Codex hook-review prompt every time.
//
// Binding a probe listener and closing it leaves a gap in which something else could take the
// port. That is accepted: the alternative is not knowing the url until after start, the window
// is microseconds on a loopback interface, and losing the race only means the next start picks
// a different port.
public static class HookPortAllocator
{
    public static int Allocate(int preferred)
    {
        if (preferred > 0 && IsFree(preferred))
            return preferred;

        using var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, port);

            probe.Start();
            probe.Stop();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
