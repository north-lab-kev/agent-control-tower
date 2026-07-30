using Act.Core.Events;

namespace Act.Core.Abstractions;

// Where an out-of-band ingestion source hands what it observed. The hook endpoint knows a task
// id (from the token) and a payload; it does not know which live session that is, and must not
// — resolving one to the other is the session registry's job, so this is the seam between them.
public interface IAgentEventSink
{
    void Publish(Guid taskId, AgentEvent agentEvent);

    // For an agent that mints its own id: the first payload naming the session is what binds it
    // to the card, so the sink has to be able to complete the binding as well as feed it.
    void Bind(Guid taskId, string sessionId);
}
