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

    // Where each live session's transcript is, learned from a hook payload rather than at launch.
    // It is here because it is per live session and arrives late, exactly like the session id.
    private readonly ConcurrentDictionary<Guid, string> transcripts = new();

    public event Action? Changed;

    // Carries the session itself, unlike `Changed`: whoever drains an event stream has to be handed
    // the stream, and it must be handed over before anything can be missed.
    public event Action<IAgentSession>? Added;

    // Raised once per session, when its transcript becomes known.
    public event Action<IAgentSession, string>? TranscriptLocated;

    public int LiveCount => sessions.Count;

    // What stops a card from binding to another card's session when both were launched in the same
    // directory: a file a live card already holds is not a candidate for anyone else.
    public IReadOnlySet<string> ClaimedTranscripts => transcripts.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IAgentSession? For(Guid cardId) => sessions.GetValueOrDefault(cardId);

    public bool IsLive(Guid cardId) => sessions.ContainsKey(cardId);

    public void Add(IAgentSession session)
    {
        sessions[session.TaskId] = session;

        Added?.Invoke(session);
        Changed?.Invoke();
    }

    // Every hook payload names the transcript, so this is called many times a session and must raise
    // once: `TryAdd` is the whole guard, and it is why a second tail can never be started for a card.
    public void LocateTranscript(Guid cardId, string path)
    {
        if (sessions.GetValueOrDefault(cardId) is not { } session)
            return;

        if (!transcripts.TryAdd(cardId, path))
            return;

        TranscriptLocated?.Invoke(session, path);
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
        transcripts.TryRemove(cardId, out _);
    }
}
