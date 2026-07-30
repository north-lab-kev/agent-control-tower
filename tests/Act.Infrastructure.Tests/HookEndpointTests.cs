using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.Infrastructure.Hooks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Tests;

public class HookEndpointTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public void Before_binding_there_is_no_address_and_no_url()
    {
        var endpoint = new HookEndpoint();

        endpoint.BaseAddress.Should().BeNull();
        endpoint.UrlFor(AgentType.ClaudeCode).Should().BeNull();
    }

    [Fact]
    public void Each_agent_gets_its_own_route_so_the_path_picks_the_parser()
    {
        var endpoint = new HookEndpoint();

        endpoint.Bind(49711);

        endpoint.UrlFor(AgentType.ClaudeCode)!.AbsolutePath.Should().Be(HookTransport.ClaudeRoute);
        endpoint.UrlFor(AgentType.Codex)!.AbsolutePath.Should().Be(HookTransport.CodexRoute);
    }

    [Fact]
    public void The_endpoint_binds_to_loopback_only()
    {
        var endpoint = new HookEndpoint();

        endpoint.Bind(49711);

        endpoint.BaseAddress!.Host.Should().Be("127.0.0.1");
    }

    // A resume reuses the card's session; a fresh token would be a new Codex hook definition and a
    // fresh trust prompt with it.
    [Fact]
    public void Registering_the_same_task_twice_returns_the_same_token()
    {
        var endpoint = new HookEndpoint();

        endpoint.Register(TaskId).Should().Be(endpoint.Register(TaskId));
    }

    [Fact]
    public void Different_tasks_get_different_tokens()
    {
        var endpoint = new HookEndpoint();

        endpoint.Register(TaskId).Should().NotBe(endpoint.Register(Guid.NewGuid()));
    }

    [Fact]
    public void A_token_resolves_to_its_task_and_an_unknown_one_does_not()
    {
        var endpoint = new HookEndpoint();

        var token = endpoint.Register(TaskId);

        endpoint.TryResolve(token, out var resolved).Should().BeTrue();
        resolved.Should().Be(TaskId);

        endpoint.TryResolve("not-a-token", out _).Should().BeFalse();
        endpoint.TryResolve(string.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void Releasing_a_task_retires_its_token()
    {
        var endpoint = new HookEndpoint();

        var token = endpoint.Register(TaskId);

        endpoint.Release(TaskId);

        endpoint.TryResolve(token, out _).Should().BeFalse();
    }

    // Not a secret by itself — the token is what authorizes — but a guessable one is a shorter
    // walk for anything local, so it stays long and random.
    [Fact]
    public void A_token_is_long_and_random()
    {
        var endpoint = new HookEndpoint();

        endpoint.Register(TaskId).Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
    }
}

// Registering ACT's infrastructure has to mean the hook endpoint too — the registrations were
// briefly split across two extensions with no principle behind the split, which is the kind of seam
// that lets a composition root wire half an endpoint and find out at runtime.
//
// What it cannot mean is the *whole* endpoint: the handler needs a normalizer per agent and a sink
// that can find a live session, and both of those live in layers above this one. That is the
// dependency direction working, so the tests below pin the boundary rather than wish it away.
public class HookEndpointRegistrationTests
{
    [Fact]
    public void One_call_wires_the_parts_infrastructure_owns()
    {
        using var directory = new TempDirectory();
        using var provider = Provider(directory.Path);

        provider.GetService<IHookEndpoint>().Should().NotBeNull();
        provider.GetService<IAgentConfigFiles>().Should().NotBeNull();
        provider.GetService<HookEndpointBinder>().Should().NotBeNull();
    }

    // Names the seam: infrastructure registers the handler, and the composition root completes it.
    [Fact]
    public void The_handler_resolves_once_the_app_supplies_a_sink_and_a_normalizer()
    {
        using var directory = new TempDirectory();

        var services = new ServiceCollection().AddActInfrastructure(directory.Path);

        using (var incomplete = services.BuildServiceProvider())
        {
            FluentActions.Invoking(incomplete.GetService<HookRequestHandler>)
                .Should().Throw<InvalidOperationException>();
        }

        services.AddSingleton<IAgentEventSink, NullSink>();
        services.AddSingleton<IHookNormalizer, NullNormalizer>();

        using var completed = services.BuildServiceProvider();

        completed.GetService<HookRequestHandler>().Should().NotBeNull();
    }

    private sealed class NullSink : IAgentEventSink
    {
        public void Publish(Guid taskId, AgentEvent agentEvent)
        {
        }

        public void Bind(Guid taskId, string sessionId)
        {
        }
    }

    private sealed class NullNormalizer : IHookNormalizer
    {
        public AgentType Agent => AgentType.ClaudeCode;

        public HookNormalization Normalize(JsonElement payload, string? knownSessionId, DateTimeOffset at)
            => HookNormalization.None;
    }

    // The concrete type and the port must be the same object, or the guard would compare requests
    // against a port nothing ever bound.
    [Fact]
    public void The_port_the_guard_reads_is_the_port_the_adapters_were_given()
    {
        using var directory = new TempDirectory();
        using var provider = Provider(directory.Path);

        provider.GetRequiredService<HookEndpoint>()
            .Should().BeSameAs(provider.GetRequiredService<IHookEndpoint>());
    }

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}

public class HookPortAllocatorTests
{
    [Fact]
    public void A_remembered_port_is_kept_when_it_is_free()
    {
        var free = HookPortAllocator.Allocate(0);

        HookPortAllocator.Allocate(free).Should().Be(free);
    }

    // Sticky, not stubborn: losing the remembered port costs a Codex review prompt, but failing to
    // start would cost the whole endpoint.
    [Fact]
    public void A_taken_port_falls_back_to_a_free_one()
    {
        var taken = HookPortAllocator.Allocate(0);

        using var squatter = new TcpListener(IPAddress.Loopback, taken);

        squatter.Start();

        var allocated = HookPortAllocator.Allocate(taken);

        allocated.Should().NotBe(taken).And.BeGreaterThan(0);

        squatter.Stop();
    }

    [Fact]
    public void With_nothing_remembered_a_port_is_chosen()
        => HookPortAllocator.Allocate(0).Should().BeGreaterThan(0);
}

// The remembered-port policy stayed in infrastructure when the web-shaped wiring moved to the app,
// which is what makes it testable at all.
public class HookEndpointBinderTests
{
    [Fact]
    public void Binding_twice_across_restarts_keeps_the_same_port()
    {
        using var directory = new TempDirectory();

        int first;

        using (var provider = Provider(directory.Path))
        {
            first = provider.GetRequiredService<HookEndpointBinder>().Bind();

            var endpoint = provider.GetRequiredService<HookEndpoint>();

            endpoint.Port.Should().Be(first);
            endpoint.BaseAddress!.Port.Should().Be(first);
        }

        // A second provider over the same store is what an ACT restart looks like.
        using var restarted = Provider(directory.Path);

        restarted.GetRequiredService<HookEndpointBinder>().Bind().Should().Be(first);
    }

    private static ServiceProvider Provider(string dataDirectory)
        => new ServiceCollection().AddActInfrastructure(dataDirectory).BuildServiceProvider();
}

public class HookPortGuardTests
{
    private const int HookPort = 49711;

    private const int AppPort = 5210;

    [Fact]
    public void A_hook_post_on_the_hook_port_is_allowed()
        => HookPortGuard.Rejects(HookPort, HookPort, HookTransport.ClaudeRoute).Should().BeFalse();

    [Fact]
    public void The_ui_on_the_app_port_is_allowed()
        => HookPortGuard.Rejects(AppPort, HookPort, "/card/abc/terminal").Should().BeFalse();

    // The direction that matters: the hook surface must not be reachable on the port the browser
    // talks to — and one day, if the board is exposed remotely, neither must it ride along.
    [Fact]
    public void A_hook_post_on_the_app_port_is_rejected()
        => HookPortGuard.Rejects(AppPort, HookPort, HookTransport.CodexRoute).Should().BeTrue();

    [Fact]
    public void The_ui_on_the_hook_port_is_rejected()
        => HookPortGuard.Rejects(HookPort, HookPort, "/").Should().BeTrue();

    // Before the endpoint binds, nothing is on the hook port; taking the UI down with it would be
    // the worse failure.
    [Fact]
    public void With_no_hook_port_bound_the_ui_still_answers_and_hooks_do_not()
    {
        HookPortGuard.Rejects(AppPort, 0, "/").Should().BeFalse();
        HookPortGuard.Rejects(AppPort, 0, HookTransport.ClaudeRoute).Should().BeTrue();
    }
}

public class HookRequestHandlerTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public void A_post_with_no_token_is_unauthorized_and_publishes_nothing()
    {
        var sink = new RecordingSink();
        var handler = Build(sink, out _);

        handler.Handle(AgentType.ClaudeCode, null, Payload()).Should().Be(HookResult.Unauthorized);
        sink.Published.Should().BeEmpty();
    }

    [Fact]
    public void A_post_with_an_unknown_token_is_unauthorized()
    {
        var sink = new RecordingSink();
        var handler = Build(sink, out _);

        handler.Handle(AgentType.ClaudeCode, "someone-elses-token", Payload())
            .Should().Be(HookResult.Unauthorized);
        sink.Published.Should().BeEmpty();
    }

    [Fact]
    public void An_authorized_post_publishes_to_the_task_the_token_names()
    {
        var sink = new RecordingSink();
        var handler = Build(sink, out var endpoint);

        var result = handler.Handle(AgentType.ClaudeCode, endpoint.Register(TaskId), Payload());

        result.Should().Be(HookResult.Accepted);
        sink.Published.Should().ContainSingle().Which.Should().Be((TaskId, "abc"));
    }

    // The self-minting agent's binding arrives this way, and only this way.
    [Fact]
    public void A_payload_that_names_a_session_binds_it()
    {
        var sink = new RecordingSink();
        var handler = Build(sink, out var endpoint);

        handler.Handle(AgentType.ClaudeCode, endpoint.Register(TaskId), Payload());

        sink.Bound.Should().ContainSingle().Which.Should().Be((TaskId, "abc"));
    }

    // An agent ACT has no normalizer for is still acked: a hook that fails is a hook that can
    // wedge the session, and ACT never blocks one.
    [Fact]
    public void An_unrecognised_payload_is_accepted_and_publishes_nothing()
    {
        var sink = new RecordingSink();
        var handler = Build(sink, out var endpoint);

        var result = handler.Handle(
            AgentType.ClaudeCode,
            endpoint.Register(TaskId),
            JsonDocument.Parse("""{ "hook_event_name": "NotAThingYet" }""").RootElement);

        result.Should().Be(HookResult.Accepted);
        sink.Published.Should().BeEmpty();
    }

    private static HookRequestHandler Build(RecordingSink sink, out HookEndpoint endpoint)
    {
        endpoint = new HookEndpoint();

        endpoint.Bind(49711);

        return new HookRequestHandler(
            endpoint,
            [new StubNormalizer()],
            sink,
            new FixedClock());
    }

    private static JsonElement Payload()
        => JsonDocument.Parse("""
            { "hook_event_name": "UserPromptSubmit", "session_id": "abc" }
            """).RootElement;

    private sealed class StubNormalizer : IHookNormalizer
    {
        public AgentType Agent => AgentType.ClaudeCode;

        public HookNormalization Normalize(JsonElement payload, string? knownSessionId, DateTimeOffset at)
        {
            if (!payload.TryGetProperty("session_id", out var session))
                return HookNormalization.None;

            var id = session.GetString()!;

            return new HookNormalization(id, [new ActivityObserved(id, at)]);
        }
    }

    private sealed class RecordingSink : IAgentEventSink
    {
        public List<(Guid TaskId, string SessionId)> Published { get; } = [];

        public List<(Guid TaskId, string SessionId)> Bound { get; } = [];

        public void Publish(Guid taskId, AgentEvent agentEvent)
            => Published.Add((taskId, agentEvent.SessionId));

        public void Bind(Guid taskId, string sessionId) => Bound.Add((taskId, sessionId));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => new(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);
    }
}
