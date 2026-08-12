using System.Net;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Spawning;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using ModelContextProtocol.Client;

namespace Act.App.E2eTests;

// The other half of what only this tier can show. `LaunchTests` proves an *event* moves the board;
// this proves an agent's **tool call** does — the MCP request arrives on ACT's loopback endpoint, a
// card is minted, and a strip the browser never asked for appears in Ready.
//
// **No real CLI and no vendor tokens.** ACT's MCP server is its own HTTP endpoint, so the agent's
// side of the wire is a client speaking the same protocol Claude Code and Codex speak — the SDK's
// own. The card is launched through the mock adapter exactly as every other spec launches one, and
// the token comes off `IHookEndpoint`, which is where a real launch would have got it too.
public sealed class FollowUpTests : BrowserTest
{
    protected override void Arrange()
    {
        App.Cards.Add(Card(1, "Rework the auth module", BoardColumn.Ready));

        App.Claude.Script = AgentScript.Start().Activity("Read").AwaitsKeystroke();
    }

    // The one worth the tier: nothing touches the browser between the launch and the new strip.
    [Fact]
    public async Task An_agents_tool_call_puts_a_card_on_the_board()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them to the new fixture.");

        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Lane("Ready").Locator("div.strip .ttl"))
            .ToHaveTextAsync("Migrate the auth tests");
    }

    // Lineage is stored on both sides, and the board is what reads it — a child that appeared with a
    // half-written link would look right here and break the parent's timeline and the archive.
    [Fact]
    public async Task The_new_card_is_linked_to_the_task_that_asked_for_it()
    {
        await GoAsync();

        var parent = await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them to the new fixture.");
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);

        var child = App.Board.In(BoardColumn.Ready).Single();

        child.ParentId.Should().Be(parent.Id);
        child.Origin.Should().Be(TaskOrigin.Spawned);
        child.SpawnAuthor.Should().Be(SpawnAuthor.Agent);
        child.AgentType.Should().Be(parent.AgentType);
        child.WorkingDir.Should().Be(parent.WorkingDir);

        App.Board.Card(parent.Id)!.Children.Should().ContainSingle().Which.Should().Be(child.Id);
    }

    // Both densities carry it, and the board is where that has to be true rather than in a unit test
    // of the face: a card the user did not write should be recognisable at a glance.
    [Fact]
    public async Task A_spawned_card_is_marked_as_spawned_on_the_board()
    {
        await GoAsync();

        var parent = await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them to the new fixture.");

        var marker = Lane("Ready").Locator("div.strip .lin");

        await Assertions.Expect(marker).ToHaveTextAsync($"↳ #{parent.Number}");
    }

    [Fact]
    public async Task The_parents_timeline_records_the_spawn()
    {
        await GoAsync();

        var parent = await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them to the new fixture.");
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);

        var row = App.Board.Card(parent.Id)!.Transitions
            .Should().ContainSingle(entry => entry.Reason == TransitionReason.SpawnedFollowUp).Which;

        row.Column.Should().BeNull();
        row.Note.Should().Contain("Migrate the auth tests");
    }

    // The read side, board-wide by decision: the caller can see the card it is running on, not only
    // what it created. Without this, `dependsOn` has nothing to name.
    [Fact]
    public async Task An_agent_can_read_the_whole_board_and_tell_its_own_work_apart()
    {
        await GoAsync();

        var parent = await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them to the new fixture.");
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);

        var listing = await CallAsync(agent, McpTransport.ListTasks, new Dictionary<string, object?>());

        listing.Should().Contain("Migrate the auth tests");
        listing.Should().Contain(parent.Title);
        listing.Should().Contain("\"spawnedByYou\":true");
    }

    // The whole reason `parentId` is not an argument: an unauthenticated caller cannot reach the
    // server at all, so it can never name a card that is not its own.
    [Fact]
    public async Task A_caller_without_a_valid_token_is_refused_the_server_entirely()
    {
        await GoAsync();
        await LaunchAsync();

        var connect = async () =>
        {
            await using var stranger = await McpClient.CreateAsync(Transport("not-a-real-token"));
        };

        await connect.Should().ThrowAsync<Exception>();

        App.Board.In(BoardColumn.Ready).Should().BeEmpty();
    }

    // The reason the token is re-read on every request instead of captured when the connection opened:
    // a streamable-HTTP session outlives many requests, and ending a card releases its token. Captured
    // once, a finished card would keep a working connection for as long as the client held the socket —
    // and could still write to the board.
    //
    // The rejection lands at the *door*, as a transport 401 rather than a tool result: the middleware
    // runs per request, so an unauthorized caller never reaches a tool at all. That is the stronger of
    // the two outcomes and the one worth pinning — `ActMcpTools` re-checks as well, but only ever gets
    // the chance in the window between the two.
    [Fact]
    public async Task A_connection_stops_working_the_moment_its_card_is_done()
    {
        await GoAsync();

        var card = await LaunchAsync();

        await using var agent = await ConnectAsync();

        await CreateFollowUpAsync(agent, "Still allowed", "While the session is live.");
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);

        // What ending a session does to the token, without needing the session to actually end.
        App.App.GetRequiredService<IHookEndpoint>().Release(card.Id);

        var write = async () => await CreateFollowUpAsync(agent, "Should never exist", "The token is gone.");

        (await write.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);
        App.Board.All.Should().NotContain(other => other.Title == "Should never exist");
    }

    // The reads sit behind the same door, so a released token cannot keep reading the board either.
    [Fact]
    public async Task A_released_token_cannot_read_the_board_either()
    {
        await GoAsync();

        var card = await LaunchAsync();

        await using var agent = await ConnectAsync();

        App.App.GetRequiredService<IHookEndpoint>().Release(card.Id);

        var list = async () => await CallAsync(agent, McpTransport.ListTasks, new Dictionary<string, object?>());
        var get = async () => await CallAsync(agent, McpTransport.GetTask, new Dictionary<string, object?>
        {
            ["id"] = card.Id.ToString(),
        });

        (await list.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await get.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Read-then-write races; a key does not. The card the second call gets back must be the first
    // one, not a twin.
    [Fact]
    public async Task A_repeated_call_carrying_the_same_key_returns_the_first_card()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        var first = await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them.", "retry-1");
        var second = await CreateFollowUpAsync(agent, "Migrate the auth tests", "Port them.", "retry-1");

        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(1);

        second.Should().Contain("\"alreadyExisted\":true");
        first.Should().Contain("\"alreadyExisted\":false");
        App.Board.In(BoardColumn.Ready).Should().ContainSingle();
    }

    // A refusal is information the agent can act on, so it comes back as a result rather than as a
    // transport failure — and nothing lands on the board.
    [Fact]
    public async Task A_bad_argument_comes_back_as_a_readable_refusal_and_creates_nothing()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        var result = await CallAsync(agent, McpTransport.CreateFollowUp, new Dictionary<string, object?>
        {
            ["title"] = "Migrate the auth tests",
            ["prompt"] = "Port them.",
            ["agent"] = "gemini",
        });

        result.Should().Contain("same, claude, codex");
        App.Board.In(BoardColumn.Ready).Should().BeEmpty();
    }

    // Three, and only three. The tool list is the agent's whole view of what ACT can be asked for, so
    // an accidental fourth would be a capability nobody decided to grant.
    [Fact]
    public async Task The_server_offers_exactly_the_three_tools_act_decided_on()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        var tools = await agent.ListToolsAsync();

        tools.Select(tool => tool.Name).Should().BeEquivalentTo(McpTransport.Tools);
    }

    // The calling task is a fact about the connection, not something a model may state. If the token
    // ever appeared in the schema it would be both spoofable and a required argument no client sends.
    [Fact]
    public async Task The_session_token_is_never_a_tool_argument()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        foreach (var tool in await agent.ListToolsAsync())
            tool.JsonSchema.ToString().Should().NotContain("token", $"{tool.Name} must not ask for one");
    }

    // With tool search enabled Claude Code loads only tool *names* and this text at session start, so
    // an empty `instructions` is the difference between the model finding the tool and not.
    [Fact]
    public async Task The_server_introduces_itself_within_the_budget_both_clis_impose()
    {
        await GoAsync();
        await LaunchAsync();

        await using var agent = await ConnectAsync();

        agent.ServerInstructions.Should().NotBeNullOrWhiteSpace();
        agent.ServerInstructions!.Length.Should().BeLessThan(2048);
        agent.ServerInstructions.Should().Contain(McpTransport.CreateFollowUp);
    }

    private async Task<Card> LaunchAsync()
    {
        await Lane("Ready").Locator("div.strip").WaitForAsync();
        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);

        return App.Board.In(BoardColumn.Executing).Single();
    }

    // The agent's side of the wire. The token is taken the way a launching adapter takes it, which is
    // also what makes this the real authorization path rather than a bypass.
    private Task<McpClient> ConnectAsync()
    {
        var card = App.Board.In(BoardColumn.Executing).Single();
        var token = App.App.GetRequiredService<IHookEndpoint>().Register(card.Id);

        return McpClient.CreateAsync(Transport(token));
    }

    private HttpClientTransport Transport(string token)
    {
        var endpoint = App.App.GetRequiredService<IHookEndpoint>().BaseAddress;

        endpoint.Should().NotBeNull("the MCP server rides the hook listener, which must be bound");

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint!, McpTransport.Route),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { [HookTransport.TokenHeader] = token },
        });
    }

    private static Task<string> CreateFollowUpAsync(
        McpClient agent,
        string title,
        string prompt,
        string? clientKey = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["prompt"] = prompt,
        };

        if (clientKey is not null)
            arguments["clientKey"] = clientKey;

        return CallAsync(agent, McpTransport.CreateFollowUp, arguments);
    }

    // The structured payload as text, which is what the model would read. Asserting on it rather than
    // on ACT's own records is deliberate for the tool results: what the agent is *told* is the
    // contract, and a field that stopped being serialized would still pass a check against the store.
    private static async Task<string> CallAsync(
        McpClient agent,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await agent.CallToolAsync(tool, arguments);

        return string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(block => block.Text));
    }

    private ILocator Lane(string name)
        => Page.Locator("div.col").Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".colname", new PageLocatorOptions { HasTextString = name }),
        });
}
