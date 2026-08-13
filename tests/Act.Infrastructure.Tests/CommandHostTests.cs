using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Infrastructure.Terminal;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

// The one place in the suite that starts real processes, and deliberately so: every property worth
// having here — that a closed stdin lets a reader terminate, that a full stderr pipe does not
// deadlock a stdout read, that a deadline actually kills something — is a property of the OS, and a
// fake would only assert that the fake behaves. The shell is the platform's own, never an agent CLI.
public class CommandHostTests
{
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Standard_output_comes_back_trimmed_of_its_trailing_newline()
    {
        var result = await RunAsync(Echo("hello"));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("hello");
    }

    // Merged streams are the failure this guards: Codex writes its banner and token count to stderr
    // and only its answer to stdout, so a host that combined them would hand back a transcript.
    [Fact]
    public async Task The_two_streams_stay_separate()
    {
        var result = await RunAsync($"{Echo("answer")} && {Echo("noise")} 1>&2");

        result.Output.Should().Be("answer");
        result.Error.Should().Be("noise");
    }

    // Both halves of the stdin contract at once: what is written arrives, and the close is what lets
    // a process that reads to end-of-input finish at all. Without it this test hangs rather than fails.
    [Fact]
    public async Task Input_is_delivered_and_the_pipe_is_closed_behind_it()
    {
        var result = await RunAsync(PassThrough, input: "a title from stdin");

        result.Succeeded.Should().BeTrue(Why(result));
        result.Output.Should().Contain("a title from stdin");
    }

    // Nothing is written, and the close still has to happen — a CLI that inspects its stdin (Codex
    // announces "Reading additional input from stdin…") waits on the handle otherwise.
    [Fact]
    public async Task A_process_reading_stdin_terminates_even_with_nothing_to_read()
    {
        var result = await RunAsync(PassThrough);

        result.Succeeded.Should().BeTrue(Why(result));
    }

    [Fact]
    public async Task A_non_zero_exit_is_reported_rather_than_thrown()
    {
        var result = await RunAsync(Fail);

        result.Succeeded.Should().BeFalse();
        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task A_run_past_its_deadline_is_killed_and_says_so()
    {
        var result = await RunAsync(Sleep, timeout: TimeSpan.FromMilliseconds(300));

        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    // The deadline has to bound the *write* too. A child that never drains its stdin leaves the
    // writer blocked on a full pipe — measured at ~4KB on Windows — and a deadline armed only around
    // the exit wait was never reached: the run hung for as long as the child lived, reporting
    // nothing. The input below is far bigger than any pipe buffer, and the child sleeps without
    // reading a byte of it.
    [Fact]
    public async Task A_child_that_never_reads_its_stdin_cannot_outlive_the_deadline()
    {
        var oversized = new string('x', 1_000_000);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await RunAsync(Sleep, input: oversized, timeout: TimeSpan.FromSeconds(1));

        started.Stop();

        result.TimedOut.Should().BeTrue();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "the deadline, not the child, decides");
    }

    // The prompt travels on stdin, and the CLIs read it as UTF-8. Left to its default, .NET encodes
    // the pipe with the console code page — CP 850 on a stock French Windows — and 'é' reaches the
    // child as 0x82. `findstr` echoes the bytes it was given, so a mangled encoding comes back as
    // mojibake instead of the word that was sent.
    [Fact]
    public async Task Stdin_reaches_the_child_as_utf8_whatever_the_console_code_page()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = await RunAsync("findstr .", input: "café été\r\n");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("café été");
    }

    // The caller's cancellation is the caller's, and must not come back looking like a deadline: a
    // page that has gone away is not a CLI that was too slow.
    [Fact]
    public async Task The_callers_own_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();

        var running = RunAsync(Sleep, cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    // A global npm install puts the CLI behind `codex.cmd`, and Windows refuses to `CreateProcess` a
    // batch file — it is not an image. So one is really run here rather than asserted about: a shim
    // that stopped being applied would fail at every launch of an npm-installed agent, and nothing
    // short of a real spawn proves it is applied.
    [Fact]
    public async Task A_batch_file_is_run_through_the_shell_because_windows_cannot_spawn_one()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var script = Path.Combine(temp.Path, "act-fake-agent.cmd");

        File.WriteAllText(script, "@echo off\r\necho from the shim\r\n");

        var result = await new CommandHost().RunAsync(
            new CommandStartInfo(
                script,
                [],
                temp.Path,
                AgentEnvironment.ForQuery(new Dictionary<string, string>(), scrub: null),
                null,
                Patient));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Be("from the shim");
    }

    // The measured shim failure: a script path with a space in it plus any further quoted argument —
    // and the Claude adapter always sends the empty `--tools ""` — made `cmd` strip the wrong quotes
    // and try to run `C:\Users\Jane`. `/s` with the whole line requoted is the shape that survives
    // both; the script echoes its arguments back so the test also proves the empty one arrived.
    [Fact]
    public async Task A_batch_file_in_a_folder_with_a_space_still_takes_quoted_arguments()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();

        var spaced = Path.Combine(temp.Path, "dir with space");

        Directory.CreateDirectory(spaced);

        var script = Path.Combine(spaced, "act-fake-agent.cmd");

        File.WriteAllText(script, "@echo off\r\necho got:%*\r\n");

        var result = await new CommandHost().RunAsync(
            new CommandStartInfo(
                script,
                ["--tools", ""],
                spaced,
                AgentEnvironment.ForQuery(new Dictionary<string, string>(), scrub: null),
                null,
                Patient));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("--tools").And.Contain("\"\"");
    }

    // PATHEXT spells the extension in uppercase, so resolution can hand back `.CMD` — which must
    // take the same shim `.cmd` does rather than depend on how the raw spawn happens to handle it.
    [Fact]
    public async Task The_shim_is_applied_whatever_case_the_extension_arrives_in()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var script = Path.Combine(temp.Path, "act-fake-agent.CMD");

        File.WriteAllText(script, "@echo off\r\necho from the shim\r\n");

        var result = await new CommandHost().RunAsync(
            new CommandStartInfo(
                script,
                [],
                temp.Path,
                AgentEnvironment.ForQuery(new Dictionary<string, string>(), scrub: null),
                null,
                Patient));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Be("from the shim");
    }

    private static Task<CommandResult> RunAsync(
        string script,
        string? input = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var (shell, flag) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c") : ("/bin/sh", "-c");

        // The inherited environment, because `CommandHost` assigns rather than merges: handed an empty
        // one a shell starts and then cannot find a single command on its own `PATH`.
        return new CommandHost().RunAsync(
            new CommandStartInfo(
                shell,
                [flag, script],
                Path.GetTempPath(),
                AgentEnvironment.ForQuery(new Dictionary<string, string>(), scrub: null),
                input,
                timeout ?? Patient),
            cancellationToken);
    }

    // These start real processes, so a failure is about the machine rather than about the code, and the
    // machine is usually a CI runner nobody can attach to. `Succeeded` collapses the exit code and the
    // timeout into one bool, so asserting it bare reports "expected True, found False" and discards the
    // only two things that could explain it.
    private static string Why(CommandResult result)
        => $"exit {result.ExitCode}, timed out {result.TimedOut}, stderr <{result.Error}>, stdout <{result.Output}>";

    private static string Echo(string text) => OperatingSystem.IsWindows() ? $"echo {text}" : $"echo '{text}'";

    // Reads to end-of-input and prints what it got. `more` is the shim-free reader `cmd` has.
    private static string PassThrough => OperatingSystem.IsWindows() ? "more" : "cat";

    private static string Fail => OperatingSystem.IsWindows() ? "exit /b 3" : "exit 3";

    private static string Sleep => OperatingSystem.IsWindows()
        ? "ping -n 20 127.0.0.1 > nul"
        : "sleep 20";
}
