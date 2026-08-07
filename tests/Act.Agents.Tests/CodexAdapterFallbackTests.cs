using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

// The arms of the Codex adapter that only a broken machine reaches: an install that is nowhere, a
// `config.toml` ACT cannot make sense of, and a profile write the filesystem refuses. Each one has a
// documented answer, and none of them may cost the user their task.
[Collection(EnvironmentCollection.Name)]
public class CodexAdapterFallbackTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string LocalAppData
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // The cheapest answer and the first one asked for: an empty setting *means* "resolve the bare
    // name", so a CLI on `PATH` must record nothing at all.
    [Fact]
    public void Codex_on_PATH_needs_no_recorded_path()
    {
        var probe = new StubProbe { OnPathNames = { CodexAdapter.DefaultBinary } };

        Codex().Locate(probe).Should().Be(AgentInstall.OnPath);
    }

    [Fact]
    public void Nothing_installed_anywhere_is_reported_as_missing()
        => Codex().Locate(new StubProbe()).Should().Be(AgentInstall.Missing);

    [Fact]
    public void A_global_npm_install_is_found_under_appdata()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");

        if (appData is not { Length: > 0 })
            return;

        var npm = Path.Combine(appData, "npm", "codex.cmd");
        var probe = new StubProbe { Files = { npm } };

        Codex().Locate(probe).Should().Be(AgentInstall.At(npm));
    }

    [Theory]
    [InlineData(".local/bin/codex.exe")]
    [InlineData(".local/bin/codex")]
    public void A_user_local_install_is_found_under_home(string relative)
    {
        var path = Path.Combine(Home, Path.Combine(relative.Split('/')));
        var probe = new StubProbe { Files = { path } };

        Codex().Locate(probe).Should().Be(AgentInstall.At(path));
    }

    // The Store build lives under a build-hash directory that changes with every upgrade, so a
    // directory that is there but holds no `codex.exe` has to be walked past rather than accepted.
    [Fact]
    public void A_store_build_directory_with_no_binary_in_it_is_walked_past()
    {
        var root = Path.Combine(LocalAppData, "OpenAI", "Codex", "bin");
        var empty = Path.Combine(root, "empty");
        var real = Path.Combine(root, "good", "codex.exe");

        var probe = new StubProbe { Files = { real } };
        probe.Directories[root] = [empty, Path.Combine(root, "good")];

        Codex().Locate(probe).Should().Be(AgentInstall.At(real));
    }

    // Parsed narrowly on purpose, which means every shape the narrow parser cannot use has to fall
    // through to the next candidate rather than throw or return something wrong.
    [Theory]
    [InlineData("CODEX_CLI_PATH")]
    [InlineData("CODEX_CLI_PATH_OTHER = 'x'")]
    [InlineData("# CODEX_CLI_PATH = 'x'")]
    [InlineData("")]
    public void A_config_line_the_narrow_parser_cannot_use_is_ignored(string line)
    {
        var probe = new StubProbe();
        probe.Texts[Path.Combine(Home, ".codex", "config.toml")] = line;

        Codex().Locate(probe).Should().Be(AgentInstall.Missing);
    }

    // The declared path is the machine's own answer rather than ACT's guess — but only if it is
    // really there. A stale entry from an uninstall must not be recorded as an install.
    [Fact]
    public void A_declared_path_that_is_not_on_disk_is_not_taken()
    {
        var probe = new StubProbe();
        probe.Texts[Path.Combine(Home, ".codex", "config.toml")] =
            @"CODEX_CLI_PATH = 'C:\gone\codex.exe'";

        Codex().Locate(probe).Should().Be(AgentInstall.Missing);
    }

    [Fact]
    public void A_declared_path_is_unquoted_before_it_is_checked()
    {
        var real = @"C:\Program Files\codex\codex.exe";

        var probe = new StubProbe { Files = { real } };
        probe.Texts[Path.Combine(Home, ".codex", "config.toml")] =
            $"""
            [general]
            CODEX_CLI_PATH = "{real}"
            """;

        Codex().Locate(probe).Should().Be(AgentInstall.At(real));
    }

    // The profile is the one path ACT writes outside its own data directory, so it is the one most
    // likely to be refused. A launch that died there would cost the user their task to buy ACT some
    // observability, so the CLI runs anyway and the card reports only what its process can say.
    [Fact]
    public async Task A_profile_write_the_filesystem_refuses_launches_without_hooks()
    {
        var pty = new StubPtyHost();
        var files = new RefusingConfigFiles(new UnauthorizedAccessException("~/.codex is read-only"));

        await using var session = await Codex(pty, files).LaunchAsync(Request());

        pty.Last.Arguments.Should().NotContain("--profile");
        pty.Last.Environment.Should().NotContainKey(AgentEnvironment.ActHookToken);
        pty.Last.Arguments[^1].Should().Be("do the thing", "the task still launches");
    }

    [Fact]
    public async Task An_io_failure_writing_the_profile_is_handled_the_same_way()
    {
        var pty = new StubPtyHost();
        var files = new RefusingConfigFiles(new IOException("the file is locked"));

        await using var session = await Codex(pty, files).LaunchAsync(Request());

        pty.Last.Arguments.Should().NotContain("--profile");
    }

    // Anything else is a bug rather than a machine ACT has to tolerate, so it is not swallowed.
    [Fact]
    public async Task A_failure_that_is_not_a_filesystem_refusal_is_not_swallowed()
    {
        var files = new RefusingConfigFiles(new InvalidOperationException("bug"));

        var launch = async () => await Codex(new StubPtyHost(), files).LaunchAsync(Request());

        await launch.Should().ThrowAsync<InvalidOperationException>();
    }

    // A card stored before `CodexCapabilities` dropped a mode still carries it, and the resolver is
    // where that is refused — never a spawn with a translation nobody chose.
    [Fact]
    public async Task A_config_the_resolver_rejects_never_reaches_the_pty()
    {
        var pty = new StubPtyHost();

        var launch = async () => await Codex(pty).LaunchAsync(
            Request(new LaunchConfig { PermissionMode = PermissionMode.Auto }));

        (await launch.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Auto");

        pty.Started.Should().BeEmpty();
    }

    // Codex reads its profile layers only out of its own config home, and ACT ships to machines whose
    // home directory it cannot know.
    [Fact]
    public void The_codex_home_environment_variable_wins_over_the_user_profile()
    {
        var original = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", @"C:\custom\codex");

            CodexHookConfig.ResolveCodexHome().Should().Be(@"C:\custom\codex");
            CodexHookConfig.ProfilePath(CodexHookConfig.ResolveCodexHome())
                .Should().StartWith(@"C:\custom\codex");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", original);
        }
    }

    [Fact]
    public void An_empty_codex_home_falls_back_to_the_user_profile()
    {
        var original = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", string.Empty);

            CodexHookConfig.ResolveCodexHome().Should().StartWith(Home);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", original);
        }
    }

    // The arm stays total rather than throwing, because a stored card may carry a mode this build no
    // longer offers — and `Auto` is the one that lands here today.
    [Fact]
    public void An_unoffered_mode_still_maps_to_something_launchable()
    {
        var adapter = Codex();

        adapter.Capabilities.PermissionModes.Should().NotContain(PermissionMode.Auto);
        adapter.Resolve(new LaunchConfig { PermissionMode = PermissionMode.Auto })
            .CanLaunch.Should().BeFalse();
    }

    private static AgentLaunchRequest Request(LaunchConfig? config = null)
        => new(
            TaskId,
            "ignored-by-codex",
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default);

    private static CodexAdapter Codex(
        StubPtyHost? pty = null,
        IAgentConfigFiles? files = null)
        => new(
            pty ?? new StubPtyHost(),
            new StubCommandHost(),
            new TestClock(),
            new StubHookEndpoint(),
            files ?? new StubAgentConfigFiles(),
            NullLogger<CodexAdapter>.Instance);

    private sealed class StubProbe : IExecutableProbe
    {
        public HashSet<string> OnPathNames { get; } = [];

        public HashSet<string> Files { get; } = [];

        public Dictionary<string, string> Texts { get; } = [];

        public Dictionary<string, string[]> Directories { get; } = [];

        public string? OnPath(string executable)
            => OnPathNames.Contains(executable) ? $"/usr/bin/{executable}" : null;

        public string? FirstExisting(IEnumerable<string> candidates)
            => candidates.FirstOrDefault(Files.Contains);

        public IReadOnlyList<string> DirectoriesNewestFirst(string parent)
            => Directories.TryGetValue(parent, out var found) ? found : [];

        public string? ReadText(string path) => Texts.GetValueOrDefault(path);
    }

    // Refuses the *external* write only — the shared forwarder still lands, which is the shape of the
    // real failure: ACT's own data directory is writable and `~/.codex` is not.
    private sealed class RefusingConfigFiles(Exception failure) : IAgentConfigFiles
    {
        private readonly StubAgentConfigFiles inner = new();

        public string Write(Guid taskId, string fileName, string content)
            => inner.Write(taskId, fileName, content);

        public string WriteShared(string fileName, string content)
            => inner.WriteShared(fileName, content);

        public string ScratchDirectory() => inner.ScratchDirectory();

        public void WriteExternal(string absolutePath, string content) => throw failure;

        public void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker)
            => throw failure;

        public void DeleteExternal(string absolutePath) => inner.DeleteExternal(absolutePath);

        public void Clear(Guid taskId) => inner.Clear(taskId);
    }
}
