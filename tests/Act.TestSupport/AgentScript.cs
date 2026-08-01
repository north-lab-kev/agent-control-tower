using Act.Core.Events;

namespace Act.TestSupport;

// A whole session lifecycle as one readable line. A step emits a normalized event, paints
// terminal output, or blocks until the matching input arrives — so a round-trip through the
// terminal can be asserted without sleeping or racing.
public sealed class AgentScript
{
    private readonly List<AgentScriptStep> steps = [];

    private AgentScript()
    {
    }

    internal IReadOnlyList<AgentScriptStep> Steps => steps;

    public static AgentScript Start(
        string transcriptPath = "transcript.jsonl",
        string workingDir = ".")
        => new AgentScript().Emit((session, at) => new SessionStarted(session, at, transcriptPath, workingDir));

    public static AgentScript Empty() => new();

    public AgentScript Activity(string? toolName = null)
        => Emit((session, at) => new ActivityObserved(session, at, toolName));

    public AgentScript Compacts()
        => Emit((session, at) => new CompactingStarted(session, at))
            .Emit((session, at) => new CompactingFinished(session, at));

    public AgentScript RequestsPermission(string summary, string requestId = "permission-1")
        => Emit((session, at) => new PermissionRequested(session, at, requestId, summary));

    public AgentScript Asks(string question, string requestId = "question-1")
        => Emit((session, at) => new QuestionAsked(session, at, requestId, question));

    // What a blocked session does in reality: it paints its prompt and waits for a human to
    // type into the terminal. Nothing ACT can answer, so the script waits on a keystroke.
    public AgentScript Paints(string output)
    {
        steps.Add(new AgentScriptStep(null, null, output));

        return this;
    }

    public AgentScript AwaitsKeystroke() => Await(AgentInputKind.Write);

    public AgentScript Enriches(EnrichmentSnapshot snapshot)
        => Emit((session, at) => new SessionEnriched(session, at, snapshot));

    public AgentScript WritesFollowUps(params string[] files)
        => Emit((session, at) => new FollowUpsWritten(session, at, files));

    public AgentScript EndsTurn()
        => Emit((session, at) => new TurnEnded(session, at));

    public AgentScript Exits(int exitCode)
        => Emit((session, at) => new ProcessExited(session, at, exitCode));

    public AgentScript SessionEnds()
        => Emit((session, at) => new SessionEnded(session, at));

    private AgentScript Emit(Func<string, DateTimeOffset, AgentEvent> factory)
    {
        steps.Add(new AgentScriptStep(factory, null));

        return this;
    }

    private AgentScript Await(AgentInputKind kind)
    {
        steps.Add(new AgentScriptStep(null, kind));

        return this;
    }
}
