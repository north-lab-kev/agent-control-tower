namespace Act.TestSupport;

// What ACT is still able to put into a session, now that prompts are answered in the agent's
// own terminal: raw keystrokes, the two messages it submits itself, and the kill hatch.
public enum AgentInputKind
{
    Write,
    Submit,
    Resize,
    Kill,
}
