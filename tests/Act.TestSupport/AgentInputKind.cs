namespace Act.TestSupport;

// What can reach a session, now that everything an agent is told after launch is typed by the
// user: their raw keystrokes on their way through, the resize, and the kill hatch. Nothing here
// is composed by ACT — `Submit` was the last of those and went with send-back on 2026-08-01.
public enum AgentInputKind
{
    Write,
    Resize,
    Kill,
}
