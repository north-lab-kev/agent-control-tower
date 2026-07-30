namespace Act.Infrastructure.Hooks;

// The remembered-port policy, kept here rather than in the app's wiring: choosing a port and
// deciding when to keep one is storage and socket business, and none of it needs a web host. The
// app is left with the one part that does — putting the address on the listener.
//
// Public class, internal constructor: the app calls `Bind`, but where the port is remembered stays
// an infrastructure detail, so `HookRegistration` supplies the store rather than the container.
public sealed class HookEndpointBinder
{
    private readonly HookEndpoint endpoint;

    private readonly HookPortStore store;

    internal HookEndpointBinder(HookEndpoint endpoint, HookPortStore store)
    {
        this.endpoint = endpoint;
        this.store = store;
    }

    // Sticky: a port that moves between ACT restarts costs the user a fresh Codex hook-review
    // prompt, so the remembered one is reused whenever it is still free.
    public int Bind()
    {
        var remembered = store.Load();
        var port = HookPortAllocator.Allocate(remembered);

        if (port != remembered)
            store.Save(port);

        endpoint.Bind(port);

        return port;
    }
}
