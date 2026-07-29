using Act.Core.Events;

namespace Act.TestSupport;

// A whole session lifecycle as one readable line. A step either emits a normalized event or
// blocks until the matching input arrives, so a permission or question round-trip can be
// asserted without sleeping or racing.
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

    public AgentScript AwaitsDecision() => Await(AgentInputKind.Permission);

    public AgentScript Asks(string question, string requestId = "question-1")
        => Emit((session, at) => new QuestionAsked(session, at, requestId, question));

    public AgentScript AwaitsAnswer() => Await(AgentInputKind.Answer);

    public AgentScript AwaitsMessage() => Await(AgentInputKind.Message);

    public AgentScript Enriches(EnrichmentSnapshot snapshot)
        => Emit((session, at) => new SessionEnriched(session, at, snapshot));

    public AgentScript WritesFollowUps(params string[] files)
        => Emit((session, at) => new FollowUpsWritten(session, at, files));

    public AgentScript EndsTurn(TurnOutcome outcome, string? question = null)
        => Emit((session, at) => new TurnEnded(session, at, outcome, question));

    public AgentScript GoesQuiet(TimeSpan idle)
        => Emit((session, at) => new NoActivityElapsed(session, at, idle));

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
