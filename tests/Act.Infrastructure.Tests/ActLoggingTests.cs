using System.Runtime.CompilerServices;
using Act.Infrastructure.Logging;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Tests;

public class ActLoggingTests
{
    [Fact]
    public void The_log_directory_sits_under_the_data_directory()
    {
        using var temp = new TempDirectory();

        ActLogDirectory.Resolve(temp.Path).Should().Be(Path.Combine(temp.Path, "logs"));
    }

    [Fact]
    public void A_logged_message_reaches_a_dated_file()
    {
        using var temp = new TempDirectory();

        Write(temp.Path, log => log.LogInformation("Card {Number} launched.", 1031));

        var file = OnlyLogFile(temp.Path);

        Path.GetFileName(file).Should().Be($"act-{DateTime.Now:yyyyMMdd}.log");
        File.ReadAllText(file).Should().Contain("Card 1031 launched.");
    }

    [Fact]
    public void A_scope_property_is_written_beside_the_message()
    {
        using var temp = new TempDirectory();

        Write(temp.Path, log =>
        {
            using (log.BeginTaskScope(1031, "4f3a"))
                log.LogInformation("Launching {Agent}.", "ClaudeCode");
        });

        var line = File.ReadAllText(OnlyLogFile(temp.Path));

        line.Should().Contain("[Task=1031 Session=4f3a] Launching ClaudeCode.");
    }

    [Fact]
    public void A_task_with_no_session_yet_carries_only_its_number()
    {
        using var temp = new TempDirectory();

        Write(temp.Path, log =>
        {
            using (log.BeginTaskScope(1031))
                log.LogInformation("Queued.");
        });

        File.ReadAllText(OnlyLogFile(temp.Path)).Should().Contain("[Task=1031] Queued.");
    }

    // `SessionRestorer` scopes a card and then calls `SessionLauncher`, which scopes the same card
    // again. The nesting is unavoidable across call paths, so what matters is that it collapses:
    // a line reading `[Task=1031 Task=1031]` would be the tell that it does not.
    [Fact]
    public void A_repeated_scope_is_not_written_twice()
    {
        using var temp = new TempDirectory();

        Write(temp.Path, log =>
        {
            using (log.BeginTaskScope(1031, "4f3a"))
            using (log.BeginTaskScope(1031, "4f3a"))
                log.LogInformation("Restoring.");
        });

        File.ReadAllText(OnlyLogFile(temp.Path)).Should().Contain("[Task=1031 Session=4f3a] Restoring.");
    }

    [Fact]
    public void An_unscoped_message_carries_no_context_group()
    {
        using var temp = new TempDirectory();

        Write(temp.Path, log => log.LogInformation("Nothing to correlate."));

        var line = File.ReadAllText(OnlyLogFile(temp.Path));

        line.Should().EndWith("Nothing to correlate." + Environment.NewLine);
        line.Should().NotContain("{}");
        line.Should().NotContain("[");
    }

    [Fact]
    public void The_provider_defers_to_the_host_log_level()
    {
        using var temp = new TempDirectory();

        Write(
            temp.Path,
            log =>
            {
                log.LogDebug("Chatter.");
                log.LogInformation("News.");
            },
            logging => logging.SetMinimumLevel(LogLevel.Information));

        var content = File.ReadAllText(OnlyLogFile(temp.Path));

        content.Should().Contain("News.");
        content.Should().NotContain("Chatter.");
    }

    [Fact]
    public void Only_the_logging_folder_names_serilog()
    {
        var source = SourceRoot();

        var offenders =
            from file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            let relative = Path.GetRelativePath(source, file)
            where !relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(Path.Combine("Act.Infrastructure", "Logging") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            where File.ReadAllText(file).Contains("Serilog", StringComparison.Ordinal)
            select relative;

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_logging_folder_is_where_it_actually_lives()
    {
        var wiring = Path.Combine(SourceRoot(), "Act.Infrastructure", "Logging", "ActLogging.cs");

        File.ReadAllText(wiring).Should().Contain("Serilog");
    }

    private static void Write(
        string dataDirectory,
        Action<ILogger> write,
        Action<ILoggingBuilder>? configure = null)
    {
        using var factory = LoggerFactory.Create(logging =>
        {
            logging.AddActFileLog(dataDirectory);

            configure?.Invoke(logging);
        });

        write(factory.CreateLogger("Act.Tests.Subject"));
    }

    private static string OnlyLogFile(string dataDirectory)
        => Directory.EnumerateFiles(ActLogDirectory.Resolve(dataDirectory), "*.log").Single();

    private static string SourceRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src"));
}
