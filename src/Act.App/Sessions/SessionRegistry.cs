using System.Collections.Concurrent;
using Act.Core.Abstractions;

namespace Act.App.Sessions;

// The live sessions, keyed by card. It exists because the terminal view is a route rather than
// a child of the board: navigating to a card has to find the process that is already running
// for it, not start a second one. Sessions outlive every component that shows them and die
// with the app, which is why this is a singleton and why it disposes what it still holds.
public sealed class SessionRegistry(IHookEndpoint hooks, IAgentConfigFiles configFiles) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, IAgentSession> sessions = new();

    public event Action? Changed;

    public int LiveCount => sessions.Count;

    public IAgentSession? For(Guid cardId) => sessions.GetValueOrDefault(cardId);

    public bool IsLive(Guid cardId) => sessions.ContainsKey(cardId);

    public void Add(IAgentSession session)
    {
        sessions[session.TaskId] = session;

        Changed?.Invoke();
    }

    public async Task<bool> EndAsync(Guid cardId)
    {
        if (!sessions.TryRemove(cardId, out var session))
            return false;

        await session.DisposeAsync();

        Retire(cardId);

        Changed?.Invoke();

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cardId in sessions.Keys.ToList())
        {
            if (sessions.TryRemove(cardId, out var session))
            {
                await session.DisposeAsync();

                Retire(cardId);
            }
        }
    }

    // A token outliving its session would keep authorizing posts for a card that is no longer
    // running, and the generated settings file still holds that token — so both go together.
    private void Retire(Guid cardId)
    {
        hooks.Release(cardId);
        configFiles.Clear(cardId);
    }
}
