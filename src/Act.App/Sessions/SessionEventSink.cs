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
public sealed class SessionEventSink(SessionRegistry sessions) : IAgentEventSink
{
    public void Publish(Guid taskId, AgentEvent agentEvent)
    {
        if (sessions.For(taskId) is PtyAgentSession session)
            session.Publish(agentEvent);
    }

    public void Bind(Guid taskId, string sessionId)
    {
        if (sessions.For(taskId) is PtyAgentSession session)
            session.BindSessionId(sessionId);
    }

    public void LocateTranscript(Guid taskId, string path) => sessions.LocateTranscript(taskId, path);
}
