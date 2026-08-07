using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;

namespace Act.App.Sessions;

// Turns "task 1234 observed this" into "this session's event stream carries this". The hook
// endpoint knows only a task id, because that is all a token can prove; resolving it to a live
// session is this side's business, and it is why the endpoint can live in infrastructure without
// knowing what a session is.
//
// An event for a task with no live session is dropped, not queued: hooks can outlive a killed
// process by a moment, and there is nothing to tell about a session that is gone.
public sealed class SessionEventSink(
    SessionRegistry sessions,
    BoardState board,
    ILogger<SessionEventSink> log) : IAgentEventSink
{
    public void Publish(Guid taskId, AgentEvent agentEvent)
    {
        if (sessions.For(taskId) is PtyAgentSession session)
            session.Publish(agentEvent);
    }

    // The card as well as the session, because a session object dies with its process. An agent whose
    // id ACT cannot pre-mint — Codex — has nowhere else for the binding to survive, and without it the
    // rail reads "reported at session start" for the whole session and a resume has no id to resume.
    // The already-bound check is what makes this cheap: every payload carries the id, several arrive a
    // second, and only the first one has anything to write.
    public void Bind(Guid taskId, string sessionId)
    {
        if (sessions.For(taskId) is not PtyAgentSession session || session.SessionId == sessionId)
            return;

        session.BindSessionId(sessionId);

        _ = PersistAsync(taskId, sessionId);
    }

    private async Task PersistAsync(Guid taskId, string sessionId)
    {
        try
        {
            if (board.Card(taskId) is not { } card || card.SessionId == sessionId)
                return;

            card.SessionId = sessionId;

            await board.UpdateAsync(card);
        }
        catch (Exception error)
        {
            log.LogError(error, "Persisting the session id for task {TaskId} failed.", taskId);
        }
    }

    public void LocateTranscript(Guid taskId, string path) => sessions.LocateTranscript(taskId, path);
}
