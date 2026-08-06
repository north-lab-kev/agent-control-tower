using System.Text.Json;
using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

// The Claude Code side of what `CodexAdapterFallbackTests` and its siblings pin for Codex: the arms
// only a broken machine or a moved payload shape reaches. Kept as one file per adapter rather than a
// shared base, because every one of these is a fact about *this* CLI.
public class ClaudeCodeFallbackTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private const string SessionId = "019ea722-d3f0-7563-b3cd-8ac7fb3f9a69";

    // The hook settings are written into ACT's own data directory, so this is a disk that is full or a
    // folder an installer locked. The task still launches: a launch that died here would cost the user
    // their work to buy ACT some observability.
    [Theory]
    [InlineData("io")]
    [InlineData("denied")]
    public async Task A_hook_settings_write_the_filesystem_refuses_launches_without_hooks(string kind)
    {
        var pty = new StubPtyHost();

        Exception failure = kind is "io"
            ? new IOException("the disk is full")
            : new UnauthorizedAccessException("the folder is locked");

        await using var session = await Adapter(pty, new RefusingConfigFiles(failure))
            .LaunchAsync(Request());

        pty.Last.Arguments.Should().NotContain("--settings");
        pty.Last.Environment.Should().NotContainKey(AgentEnvironment.ActHookToken);
        pty.Last.Arguments.Should().Contain("do the thing", "the task still launches");
    }

    [Fact]
    public async Task A_failure_that_is_not_a_filesystem_refusal_is_not_swallowed()
    {
        var launch = async () => await Adapter(
                new StubPtyHost(),
                new RefusingConfigFiles(new InvalidOperationException("bug")))
            .LaunchAsync(Request());

        await launch.Should().ThrowAsync<InvalidOperationException>();
    }

    // Every mode the capability list offers has to reach the CLI as the spelling the CLI documents —
    // a mode that fell through to `default` would run with more or fewer permissions than the card says.
    [Theory]
    [InlineData(PermissionMode.Default, "default")]
    [InlineData(PermissionMode.Plan, "plan")]
    [InlineData(PermissionMode.AcceptEdits, "acceptEdits")]
    [InlineData(PermissionMode.Auto, "auto")]
    [InlineData(PermissionMode.DontAsk, "dontAsk")]
    [InlineData(PermissionMode.Bypass, "bypassPermissions")]
    public async Task Every_permission_mode_reaches_the_cli_as_its_own_flag_value(
        PermissionMode mode,
        string flag)
    {
        var pty = new StubPtyHost();

        await using var session = await Adapter(pty)
            .LaunchAsync(Request(new LaunchConfig { PermissionMode = mode }));

        pty.Last.Arguments.Should().ContainInOrder("--permission-mode", flag);
    }

    [Fact]
    public void Claude_code_offers_every_mode_these_flags_cover()
        => Adapter(new StubPtyHost()).Capabilities.PermissionModes
            .Should().BeEquivalentTo(Enum.GetValues<PermissionMode>());

    // ── the usage dialect ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_dialect_answers_for_claude_code()
    {
        var dialect = new ClaudeCodeUsageDialect();

        dialect.Agent.Should().Be(AgentType.ClaudeCode);
        dialect.DefaultEndpoint.Should().Be("https://api.anthropic.com/api/oauth/usage");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "claudeAiOauth": null }""")]
    [InlineData("""{ "claudeAiOauth": "a string" }""")]
    [InlineData("""{ "claudeAiOauth": [] }""")]
    [InlineData("""{ "claudeAiOauth": { } }""")]
    [InlineData("""{ "claudeAiOauth": { "accessToken": "" } }""")]
    public void A_credential_file_with_no_usable_token_reports_missing(string credentials)
        => new ClaudeCodeUsageDialect().Token(credentials, Now).Should().Be(UsageToken.Missing);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "something_else": true }""")]
    public void A_response_neither_surface_recognises_is_nothing(string response)
        => new ClaudeCodeUsageDialect().Parse(response, Now).Should().BeNull();

    // The `limits` array carries entries ACT has no window for, and a CLI upgrade can add more. An
    // entry that is not an object at all must be walked past rather than trip the parse.
    [Fact]
    public void A_limits_entry_that_is_not_an_object_is_walked_past()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(
            """
            {
              "limits": [
                7,
                null,
                "a string",
                [],
                { "kind": "opus_weekly", "percent": 50 },
                { "kind": "session", "percent": 40, "resets_at": "2026-08-02T17:00:00Z" }
              ]
            }
            """,
            Now);

        usage.Should().NotBeNull();

        var session = usage.Of(UsageWindowKind.Session);

        session.Should().NotBeNull();
        session.Percent.Should().Be(40);
        session.ResetsAt.Should().Be(new DateTimeOffset(2026, 8, 2, 17, 0, 0, TimeSpan.Zero));
    }

    // A read that succeeded always answers with both account windows: a missing entry means nothing has
    // run in that window, which is a meter reading 0% rather than a gap where the quota should be.
    [Fact]
    public void A_response_naming_one_window_still_answers_for_both()
    {
        var usage = new ClaudeCodeUsageDialect().Parse(
            """{ "limits": [{ "kind": "session", "percent": 40 }] }""",
            Now)!;

        usage.Windows.Select(window => window.Kind)
            .Should().Equal(UsageWindowKind.Session, UsageWindowKind.Weekly);

        usage.Of(UsageWindowKind.Weekly)!.Percent.Should().Be(0);
        usage.Of(UsageWindowKind.Weekly)!.ResetsAt.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "five_hour": { "resets_at": "2026-08-02T17:00:00Z" } }""")]
    [InlineData("""{ "five_hour": { "utilization": null } }""")]
    [InlineData("""{ "five_hour": { "utilization": "40" } }""")]
    [InlineData("""{ "limits": [{ "kind": "session" }] }""")]
    [InlineData("""{ "limits": [{ "kind": "session", "percent": "40" }] }""")]
    public void A_percentage_ACT_cannot_read_is_zero_rather_than_a_missing_window(string response)
        => new ClaudeCodeUsageDialect().Parse(response, Now)!
            .Of(UsageWindowKind.Session)!.Percent.Should().Be(0);

    // `limits` wins per window where both surfaces describe the same one: `kind` names the window
    // explicitly and `percent` is unambiguously 0–100.
    [Fact]
    public void The_limits_array_wins_over_the_named_window_for_the_same_kind()
        => new ClaudeCodeUsageDialect().Parse(
                """
                {
                  "limits": [{ "kind": "session", "percent": 40 }],
                  "five_hour": { "utilization": 99 }
                }
                """,
                Now)!
            .Of(UsageWindowKind.Session)!.Percent.Should().Be(40);

    // ── the hook normalizer ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_hook_normalizer_answers_for_claude_code()
        => new ClaudeCodeHookNormalizer().Agent.Should().Be(AgentType.ClaudeCode);

    [Theory]
    [InlineData("\"a string\"")]
    [InlineData("[]")]
    [InlineData("7")]
    public void A_payload_that_is_not_an_object_normalizes_to_nothing(string json)
        => Hook(json).Should().Be(HookNormalization.None);

    // Not nothing: it may still be the first thing that says where the transcript is, which is the
    // only way a tail ever starts.
    [Fact]
    public void A_payload_with_no_event_name_still_reports_the_transcript_and_the_session()
    {
        var result = Hook("""
            { "session_id": "019f-abc", "transcript_path": "/sessions/019f-abc.jsonl" }
            """);

        result.SessionId.Should().Be("019f-abc");
        result.TranscriptPath.Should().Be("/sessions/019f-abc.jsonl");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void A_session_end_is_reported()
        => Hook("""{ "hook_event_name": "SessionEnd", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<SessionEnded>();

    // Claude Code reports only the start of a compaction; there is no `PostCompact`, which is why the
    // card leaves `compacting` on the next thing that reports activity instead.
    [Fact]
    public void A_compaction_start_is_reported()
        => Hook("""{ "hook_event_name": "PreCompact", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<CompactingStarted>();

    [Fact]
    public void An_unknown_event_normalizes_to_nothing_rather_than_failing()
        => Hook("""{ "hook_event_name": "SubagentStop", "session_id": "abc" }""")
            .Events.Should().BeEmpty();

    // ── the transcript normalizer ────────────────────────────────────────────────────────────────

    [Fact]
    public void The_transcript_normalizer_answers_for_claude_code()
        => new ClaudeCodeTranscriptNormalizer().Agent.Should().Be(AgentType.ClaudeCode);

    // Enrichment only and no events, so every unreadable line has to leave the snapshot exactly as it
    // was rather than reset a figure the card is already showing.
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "type": "user", "message": { "usage": { "input_tokens": 5 } } }""")]
    [InlineData("""{ "type": "assistant" }""")]
    [InlineData("""{ "type": "assistant", "message": null }""")]
    [InlineData("""{ "type": "assistant", "message": "a string" }""")]
    [InlineData("""{ "type": "assistant", "message": [] }""")]
    public void A_line_ACT_cannot_read_leaves_the_snapshot_alone(string line)
    {
        var known = new EnrichmentSnapshot { TokensIn = 900, TurnCount = 3 };

        var fold = new ClaudeCodeTranscriptNormalizer().Fold(known, [line]);

        fold.Snapshot.Should().Be(known);
        fold.Events.Should().BeEmpty();
    }

    // ── the usage refresher ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_refresher_answers_for_claude_code()
        => new ClaudeCodeUsageRefresher(() => Adapter(new StubPtyHost()), () => null)
            .Agent.Should().Be(AgentType.ClaudeCode);

    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static HookNormalization Hook(string json)
        => new ClaudeCodeHookNormalizer().Normalize(JsonDocument.Parse(json).RootElement, null, Now);

    private static AgentLaunchRequest Request(LaunchConfig? config = null)
        => new(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default);

    private static ClaudeCodeAdapter Adapter(StubPtyHost pty, IAgentConfigFiles? files = null)
        => new(
            pty,
            new StubCommandHost(),
            new TestClock(),
            new StubHookEndpoint(),
            files ?? new StubAgentConfigFiles(),
            NullLogger<ClaudeCodeAdapter>.Instance);

    // Refuses the per-task write only, which is the shape of the real failure: the folder exists and
    // the file cannot be created in it.
    private sealed class RefusingConfigFiles(Exception failure) : IAgentConfigFiles
    {
        private readonly StubAgentConfigFiles inner = new();

        public string Write(Guid taskId, string fileName, string content) => throw failure;

        public string WriteShared(string fileName, string content)
            => inner.WriteShared(fileName, content);

        public string ScratchDirectory() => inner.ScratchDirectory();

        public void WriteExternal(string absolutePath, string content)
            => inner.WriteExternal(absolutePath, content);

        public void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker)
            => inner.WriteExternalPreservingTail(absolutePath, content, tailMarker);

        public void DeleteExternal(string absolutePath) => inner.DeleteExternal(absolutePath);

        public void Clear(Guid taskId) => inner.Clear(taskId);
    }
}
