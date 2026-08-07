using System.Collections.Concurrent;
using Act.Core.Abstractions;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// The live sessions, keyed by card. It exists because the terminal view is a route rather than
// a child of the board: navigating to a card has to find the process that is already running
// for it, not start a second one. Sessions outlive every component that shows them and die
// with the app, which is why this is a singleton and why it disposes what it still holds.
public sealed class SessionRegistry(
    IHookEndpoint hooks,
    IAgentConfigFiles configFiles,
    ILogger<SessionRegistry> log) : IAsyncDisposable
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

    // Ending one is spelled `EndAsync` wherever it is asked for, whatever is known about it. The
    // second overload is the same operation with the *instance* in hand: a restart ends a session
    // and starts another for the same card within the same breath, and the outgoing one's own
    // teardown arrives afterwards — matched on the instance it cannot take the newcomer with it,
    // and `TryRemove` on the pair is what makes the check and the removal one step.
    public Task<bool> EndAsync(Guid cardId)
        => sessions.TryRemove(cardId, out var session)
            ? EndedAsync(cardId, session)
            : Task.FromResult(false);

    public Task<bool> EndAsync(IAgentSession session)
        => sessions.TryRemove(new KeyValuePair<Guid, IAgentSession>(session.TaskId, session))
            ? EndedAsync(session.TaskId, session)
            : Task.FromResult(false);

    // Already out of the dictionary by the time this runs — the two overloads differ only in how
    // they decide that, and this is everything they then do the same.
    private async Task<bool> EndedAsync(Guid cardId, IAgentSession session)
    {
        await session.DisposeAsync();

        Forget(cardId);

        using (log.BeginTaskScope(session: session.SessionId))
            log.LogInformation("Session ended for task {TaskId}; {Live} still live.", cardId, sessions.Count);

        Changed?.Invoke();

        return true;
    }

    // No `Changed`, and no reason to raise one: the app is going away with the sessions.
    public async ValueTask DisposeAsync()
    {
        foreach (var cardId in sessions.Keys.ToList())
        {
            if (sessions.TryRemove(cardId, out var session))
            {
                await session.DisposeAsync();

                Forget(cardId);
            }
        }
    }

    // What ACT was holding on the session's behalf rather than the session itself, which is why it
    // is a separate verb: a token outliving its session would keep authorizing posts for a card
    // that is no longer running, and the generated settings file still holds that token.
    private void Forget(Guid cardId)
    {
        hooks.Release(cardId);
        configFiles.Clear(cardId);
        transcripts.TryRemove(cardId, out _);
    }
}
