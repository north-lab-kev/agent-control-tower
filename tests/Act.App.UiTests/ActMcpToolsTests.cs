using System.Text.Json;
using Act.App.Mcp;
using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Spawning;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Act.App.UiTests;

// The in-tool authorization is the half the middleware cannot cover: a streamable-HTTP session
// outlives many requests, and ending a card releases its token mid-connection — so every call
// re-resolves the caller, and a refusal is a payload the model can read rather than a transport
// failure. The E2E suite never reaches these branches, because an unauthorized request dies at the
// middleware's 401 before a tool runs.
public class ActMcpToolsTests : ComponentTest
{
    private readonly HttpContextAccessor context = new();

    [Fact]
    public async Task A_call_with_no_request_behind_it_is_refused_not_thrown()
    {
        var tools = Tools();

        context.HttpContext = null;

        Refusal(await tools.CreateFollowUpAsync("A title", "A prompt")).Should().Contain("not authorized");
        Refusal(tools.ListTasks()).Should().Contain("not authorized");
        Refusal(tools.GetTask(Guid.NewGuid().ToString())).Should().Contain("not authorized");
    }

    [Fact]
    public async Task A_request_with_no_token_is_refused()
    {
        var tools = Tools();

        context.HttpContext = new DefaultHttpContext();

        Refusal(await tools.CreateFollowUpAsync("A title", "A prompt")).Should().Contain("not authorized");
        Refusal(tools.ListTasks()).Should().Contain("not authorized");
    }

    [Fact]
    public async Task A_live_token_reaches_the_board()
    {
        var card = await ACallingCard();
        var tools = Tools();

        Present(Hooks().Register(card.Id));

        var created = await tools.CreateFollowUpAsync("Migrate the tests", "Port them.");

        created.Should().BeOfType<FollowUpCreated>();
        Board.In(BoardColumn.Ready).Should().ContainSingle();
        tools.ListTasks().Should().BeOfType<TaskListing>();
    }

    // The window the per-call re-check exists to close: the connection stays open, the card ends,
    // and the very next call on the same socket must be refused.
    [Fact]
    public async Task A_released_token_is_refused_on_the_next_call()
    {
        var card = await ACallingCard();
        var tools = Tools();
        var hooks = Hooks();

        Present(hooks.Register(card.Id));

        tools.ListTasks().Should().BeOfType<TaskListing>();

        hooks.Release(card.Id);

        Refusal(tools.ListTasks()).Should().Contain("not authorized");
        Refusal(await tools.CreateFollowUpAsync("A title", "A prompt")).Should().Contain("not authorized");
    }

    [Fact]
    public async Task An_unknown_task_id_is_a_readable_failure()
    {
        var card = await ACallingCard();
        var tools = Tools();

        Present(Hooks().Register(card.Id));

        Refusal(tools.GetTask(Guid.NewGuid().ToString())).Should().Contain("No task on the board");
    }

    private ActMcpTools Tools() => new(FollowUps, Hooks(), context);

    private StubHookEndpoint Hooks() => (StubHookEndpoint)Services.GetRequiredService<Act.Core.Abstractions.IHookEndpoint>();

    private void Present(string token)
    {
        var http = new DefaultHttpContext();

        http.Request.Headers[HookTransport.TokenHeader] = token;

        context.HttpContext = http;
    }

    private async Task<Card> ACallingCard()
    {
        var card = new Card
        {
            Number = 1039,
            Title = "The caller",
            Column = BoardColumn.Executing,
            AgentType = AgentType.ClaudeCode,
            WorkingDir = "/dev/act",
            LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel },
        };

        await BoardWith(card);

        return card;
    }

    // The refusal is an anonymous `{ ok, errors }` payload; read through the serializer so the test
    // asserts the shape the model receives rather than a type it cannot name.
    private static string Refusal(object outcome)
    {
        var json = JsonSerializer.SerializeToElement(outcome);

        json.GetProperty("ok").GetBoolean().Should().BeFalse();

        return string.Join(" ", json.GetProperty("errors").EnumerateArray().Select(entry => entry.GetString()));
    }
}
