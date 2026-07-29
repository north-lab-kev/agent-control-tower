using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Step 6's verify line: a scripted session driven end to end through the seam. Columns and
// badges do not move yet — the rules engine that reads these events arrives at step 9.
public class MockAgentSessionTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private const string SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11";

    [Fact]
    public async Task A_turn_that_needed_a_decision_reports_every_step_in_order()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start()
                .Activity("Read")
                .RequestsPermission("push the current branch")
                .AwaitsDecision()
                .Activity("Bash")
                .EndsTurn(TurnOutcome.ReadyForReview)
                .SessionEnds(),
        };

        await using var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);

            if (received is PermissionRequested request)
                await session.RespondToPermissionAsync(request.RequestId, PermissionDecision.Allow());
        }

        seen.Select(received => received.GetType()).Should().Equal(
        [
            typeof(SessionStarted),
            typeof(ActivityObserved),
            typeof(PermissionRequested),
            typeof(ActivityObserved),
            typeof(TurnEnded),
            typeof(SessionEnded),
        ]);

        seen.Should().AllSatisfy(received => received.SessionId.Should().Be(SessionId));
        seen.Select(received => received.At).Should().BeInAscendingOrder();

        session.Received.Should().ContainSingle()
            .Which.Decision!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task A_turn_that_ends_needing_input_carries_the_question()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().EndsTurn(TurnOutcome.NeedsInput, "which branch should I push?"),
        };

        await using var session = await LaunchAsync(adapter);

        var turn = await LastAsync<TurnEnded>(session);

        turn.Outcome.Should().Be(TurnOutcome.NeedsInput);
        turn.Question.Should().Be("which branch should I push?");
    }

    [Fact]
    public async Task Answering_a_question_lets_the_turn_continue()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start()
                .Asks("which branch should I push?")
                .AwaitsAnswer()
                .EndsTurn(TurnOutcome.ReadyForReview),
        };

        await using var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);

            if (received is QuestionAsked question)
                await session.AnswerAsync(question.RequestId, "main");
        }

        seen.OfType<TurnEnded>().Should().ContainSingle()
            .Which.Outcome.Should().Be(TurnOutcome.ReadyForReview);
        session.Received.Should().ContainSingle()
            .Which.Text.Should().Be("main");
    }

    [Fact]
    public async Task Killing_a_live_session_ends_the_stream_with_a_kill()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().Activity().AwaitsMessage().EndsTurn(TurnOutcome.ReadyForReview),
        };

        await using var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);

            if (received is ActivityObserved)
                await session.KillAsync();
        }

        seen.Last().Should().BeOfType<SessionKilled>();
        seen.Should().NotContain(received => received is TurnEnded);
    }

    [Fact]
    public async Task Disposing_a_live_session_completes_the_stream()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().AwaitsMessage().EndsTurn(TurnOutcome.ReadyForReview),
        };

        var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);
            await session.DisposeAsync();
        }

        seen.Should().ContainSingle().Which.Should().BeOfType<SessionStarted>();
    }

    [Fact]
    public async Task A_launch_carries_the_pre_minted_session_id_and_the_preamble()
    {
        var adapter = new MockAgentAdapter();

        await using var session = await LaunchAsync(adapter);

        session.SessionId.Should().Be(SessionId);
        adapter.Launches.Should().ContainSingle().Which.Preamble.Should().Contain(
            ActContract.RelativeStatusDirectory);
    }

    [Fact]
    public async Task Resuming_reuses_the_same_session_id()
    {
        var adapter = new MockAgentAdapter();

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            AgentPreamble.Compose(TaskId),
            "do the thing",
            "actually target main",
            new LaunchConfig()));

        session.SessionId.Should().Be(SessionId);
        adapter.Resumes.Should().ContainSingle().Which.Message.Should().Be("actually target main");
    }

    [Fact]
    public async Task A_config_the_adapter_cannot_honour_never_launches()
    {
        var adapter = new MockAgentAdapter();
        var request = Launch(new LaunchConfig { Model = "no-such-model" });

        var launch = async () => await adapter.LaunchAsync(request);

        await launch.Should().ThrowAsync<InvalidOperationException>()
            .Where(error => error.Message.Contains("no-such-model"));
        adapter.Launches.Should().BeEmpty();
    }

    private static async Task<MockAgentSession> LaunchAsync(MockAgentAdapter adapter)
        => (MockAgentSession)await adapter.LaunchAsync(Launch());

    private static AgentLaunchRequest Launch(LaunchConfig? config = null) => new(
        TaskId,
        SessionId,
        "C:/repo",
        AgentPreamble.Compose(TaskId),
        "do the thing",
        config ?? new LaunchConfig());

    private static async Task<TEvent> LastAsync<TEvent>(IAgentSession session)
        where TEvent : AgentEvent
    {
        TEvent? last = null;

        await foreach (var received in session.Events)
        {
            if (received is TEvent match)
                last = match;
        }

        last.Should().NotBeNull();

        return last;
    }
}
