namespace Act.TestSupport;

// What can reach a session: everything an agent is told after launch is typed by the user, so
// this is their raw keystrokes on their way through, and the resize. Nothing here is composed
// by ACT.
public enum AgentInputKind
{
    Write,
    Resize,
}
