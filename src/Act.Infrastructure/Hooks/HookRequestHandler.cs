using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Hooks;

// The whole of what ACT does with a hook post: authorize it, normalize it, publish it. It answers
// success for anything it recognises *and* anything it does not, because a hook that fails is a
// hook that can wedge the agent — ACT observes, it never decides. Nothing is ever written back
// beyond an empty ack: no `permissionDecision`, no `decision: block`, no non-zero exit.
public sealed class HookRequestHandler(
    IHookEndpoint endpoint,
    IEnumerable<IHookNormalizer> normalizers,
    IAgentEventSink sink,
    IClock clock,
    ILogger<HookRequestHandler> log)
{
    private readonly Dictionary<AgentType, IHookNormalizer> byAgent =
        normalizers.ToDictionary(normalizer => normalizer.Agent);

    public HookResult Handle(AgentType agent, string? token, JsonElement payload)
    {
        if (token is null || !endpoint.TryResolve(token, out var taskId))
        {
            log.LogWarning("Hook post for {Agent} rejected: unknown or missing token.", agent);

            return HookResult.Unauthorized;
        }

        if (!byAgent.TryGetValue(agent, out var normalizer))
            return HookResult.Accepted;

        var normalized = normalizer.Normalize(payload, null, clock.Now);

        if (normalized.SessionId is { Length: > 0 } sessionId)
            sink.Bind(taskId, sessionId);

        if (normalized.TranscriptPath is { Length: > 0 } transcript)
            sink.LocateTranscript(taskId, transcript);

        foreach (var observed in normalized.Events)
            sink.Publish(taskId, observed);

        // An ingestion path nobody can see is one nobody can debug — and whether these arrive at
        // all is the open question for both agents at this step. Blocked-card events log what they
        // are blocked on, because that text is what decided the badge and is the first thing to
        // check when a card lands in the wrong column.
        log.LogInformation(
            "Hook post for {Agent} on task {TaskId} accepted: {Count} event(s) {Events}.{Detail}",
            agent,
            taskId,
            normalized.Events.Count,
            string.Join(", ", normalized.Events.Select(observed => observed.GetType().Name)),
            Detail(normalized));

        return HookResult.Accepted;
    }

    private static string Detail(HookNormalization normalized)
    {
        var waiting = normalized.Events
            .Select(observed => observed switch
            {
                PermissionRequested permission => $"permission: {permission.Summary}",
                QuestionAsked question => $"question: {question.Question}",
                _ => null,
            })
            .FirstOrDefault(text => text is not null);

        return waiting is null ? string.Empty : $" {waiting}";
    }
}

public enum HookResult
{
    Accepted,
    Unauthorized,
}
