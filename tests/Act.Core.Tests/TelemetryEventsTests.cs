using Act.Core.Model;
using Act.Core.Telemetry;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class TelemetryEventsTests
{
    // The promise on the switch, as a test: the two factories that are handed a whole object carry
    // nothing off it but counts, flags and enum names. A card holds the prompt, the title and the
    // working directory; settings hold every template's text and every agent's binary path.
    [Fact]
    public void A_launch_carries_nothing_the_user_typed()
    {
        var card = Loaded();

        var launched = TelemetryEvents.TaskLaunched(card);

        Values(launched).Should().NotContain(card.Title);
        Values(launched).Should().NotContain(card.InitialPrompt);
        Values(launched).Should().NotContain(card.WorkingDir);
        launched.Properties[TelemetryProperties.AttachmentCount].Should().Be(1);
        launched.Properties[TelemetryProperties.Agent].Should().Be(AgentType.Codex);
    }

    [Fact]
    public void A_startup_carries_template_and_agent_counts_but_not_their_text()
    {
        var snapshot = Started();

        Values(snapshot).Should().NotContain("Nightly sweep");
        Values(snapshot).Should().NotContain("Run the linter and fix what it finds.");
        Values(snapshot).Should().NotContain(@"C:\tools\claude.cmd");
        snapshot.Properties[TelemetryProperties.TemplateCount].Should().Be(2);
        snapshot.Properties[TelemetryProperties.EnabledAgents].Should().BeEquivalentTo(new[] { "ClaudeCode" });
    }

    // One event, not two: the meter counts events rather than bytes, so the settings ride the start.
    [Fact]
    public void A_startup_carries_the_run_and_the_settings_in_one_event()
    {
        var started = Started();

        started.Properties.Should().ContainKey(TelemetryProperties.AppVersion);
        started.Properties.Should().ContainKey(TelemetryProperties.MaxConcurrent);
        started.Properties.Should().ContainKey(TelemetryProperties.Theme);
    }

    // Four events were built and taken back out on 2026-08-05 — three dropped for scope or volume,
    // and `settings_snapshot` folded into `app_started` because the meter counts events, not bytes.
    // This is what stops any of them coming back by habit; see *What telemetry may carry* in
    // `docs/design-notes.md` for which went why.
    [Fact]
    public void The_events_that_were_removed_stay_removed()
    {
        TelemetryEvents.Names.All.Should().NotContain(
            ["task_completed", "agent_discovered", "app_stopped", "settings_snapshot"]);

        TelemetryProperties.All.Should().NotContain(
        [
            "turn_count",
            "tool_calls",
            "tokens_total",
            "compactions",
            "duration_seconds",
            "transition_count",
            "found",
            "path_configured",
            "resumed",
            "uptime_seconds",
        ]);
    }

    // Everything a call site can build has to survive its own sanitizer: a factory that emitted a key
    // nobody declared would be silently stripped in production and this is where that shows up.
    [Fact]
    public void Every_factory_survives_the_sanitizer_intact()
    {
        foreach (var telemetry in Everything())
        {
            var sanitized = TelemetryPayload.Sanitize(telemetry);

            sanitized.Should().NotBeNull();

            var declared = telemetry.Properties.Where(property => property.Value is not null).Select(property => property.Key);

            sanitized!.Properties.Keys.Should().BeEquivalentTo(declared);
        }
    }

    // The other direction, and the one that catches a trim leaving litter behind: a key nobody emits
    // is a key a reviewer has to reason about for nothing.
    [Fact]
    public void Every_declared_property_is_actually_emitted_by_something()
    {
        var emitted = Everything().SelectMany(telemetry => telemetry.Properties.Keys).ToHashSet(StringComparer.Ordinal);

        TelemetryProperties.All.Should().BeSubsetOf(emitted);
    }

    private static TelemetryEvent[] Everything() =>
    [
        Started(),
        TelemetryEvents.AppError(Thrown("boom"), fatal: true),
        TelemetryEvents.TaskLaunched(Loaded()),
    ];

    private static TelemetryEvent Started()
        => TelemetryEvents.AppStarted("0.4.1", "Microsoft Windows NT 10.0.26200.0", "en-CA", Configured());

    [Fact]
    public void A_crash_report_names_the_exception_type_but_never_its_message()
    {
        var error = Thrown(@"Cannot open C:\Users\someone\secret.txt");

        var reported = TelemetryEvents.AppError(error, fatal: false);

        reported.Properties[TelemetryProperties.Exception].Should().Be("System.InvalidOperationException");
        reported.Properties[TelemetryProperties.Frames].Should().BeOfType<string[]>().Which.Should().NotBeEmpty();
        Values(reported).Should().NotContain(error.Message);
    }

    // The report the first live crash produced was `System.AggregateException` and nothing else: an
    // unobserved task hands the handler a wrapper that carries neither the real type nor a stack of
    // its own, so `frames` came out empty and was dropped on the way past the sanitizer.
    [Fact]
    public void A_wrapped_failure_reports_the_exception_that_actually_failed()
    {
        var reported = TelemetryEvents.AppError(new AggregateException(Thrown("disk gone")), fatal: false);

        reported.Properties[TelemetryProperties.Exception].Should().Be("System.InvalidOperationException");
        reported.Properties[TelemetryProperties.ExceptionChain].Should()
            .BeEquivalentTo(new[] { "System.AggregateException", "System.InvalidOperationException" });
    }

    [Fact]
    public void A_wrapped_failure_still_reports_the_inner_stack()
    {
        var reported = TelemetryEvents.AppError(new AggregateException(Thrown("disk gone")), fatal: false);

        reported.Properties[TelemetryProperties.Frames].Should().BeOfType<string[]>().Which.Should().NotBeEmpty();
    }

    [Fact]
    public void An_unwrapped_failure_reports_no_chain()
    {
        TelemetryEvents.AppError(Thrown("boom"), fatal: false)
            .Properties[TelemetryProperties.ExceptionChain].Should().BeNull();
    }

    // What the crash reports were missing. The file name and line come from ACT's own PDB, which ships
    // beside the binary — they describe this repository, not anything of the user's.
    [Fact]
    public void A_frame_carries_the_source_file_and_line()
    {
        var frames = (string[])TelemetryEvents.AppError(Thrown("boom"), fatal: false)
            .Properties[TelemetryProperties.Frames]!;

        frames[0].Should().Contain(nameof(TelemetryEventsTests));
        frames[0].Should().MatchRegex(@"\(TelemetryEventsTests\.cs:\d+\)$");
    }

    // The base name only. An absolute path would be the build machine's and says nothing useful — and
    // the sanitizer would drop the whole frame for containing a separator, losing the line with it.
    [Fact]
    public void A_frame_never_carries_a_path_and_survives_the_sanitizer()
    {
        var reported = TelemetryEvents.AppError(Thrown("boom"), fatal: false);
        var frames = (string[])reported.Properties[TelemetryProperties.Frames]!;

        frames.Should().OnlyContain(frame => TelemetryPayload.IsSymbol(frame));

        var sanitized = TelemetryPayload.Sanitize(reported);

        sanitized!.Properties[TelemetryProperties.Frames].Should()
            .BeOfType<string[]>().Which.Should().HaveCount(frames.Length);
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }

    private static IEnumerable<string> Values(TelemetryEvent telemetry)
        => telemetry.Properties.Values.SelectMany(Flattened);

    private static IEnumerable<string> Flattened(object? value) => value switch
    {
        IEnumerable<string> many => many,
        null => [],
        _ => [value.ToString() ?? string.Empty],
    };

    private static Card Loaded() => new()
    {
        Title = "Rename the widget",
        InitialPrompt = "Rename Widget to Gadget everywhere.",
        WorkingDir = @"C:\Dev\north-lab-kev\agent-control-tower",
        AgentType = AgentType.Codex,
        Origin = TaskOrigin.Manual,
        Attachments = [new TaskAttachment { FileName = "spec.png" }],
    };

    private static UserSettings Configured() => new()
    {
        Templates =
        [
            new TaskTemplate { Id = Guid.NewGuid(), IsDefault = true },
            new TaskTemplate { Id = Guid.NewGuid(), Name = "Nightly sweep", Prompt = "Run the linter and fix what it finds." },
        ],
        Agents =
        [
            new AgentDefaults { Agent = AgentType.ClaudeCode, Enabled = true, Binary = @"C:\tools\claude.cmd" },
            new AgentDefaults { Agent = AgentType.Codex, Enabled = false },
        ],
    };
}
