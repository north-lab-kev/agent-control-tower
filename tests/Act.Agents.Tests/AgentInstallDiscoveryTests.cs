using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Discovery against a fake filesystem, so the result does not depend on what happens to be
// installed on the machine running the suite. The candidate paths themselves were measured on a
// real install — Claude Code at `~/.local/bin`, Codex under a build-hash folder with its path
// declared in `~/.codex/config.toml`.
public class AgentInstallDiscoveryTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void An_agent_on_PATH_needs_no_recorded_path()
    {
        var probe = new FakeProbe { OnPathNames = { "claude" } };

        var install = Claude().Locate(probe);

        install.Found.Should().BeTrue();
        install.ExplicitPath.Should().BeNull();
    }

    [Fact]
    public void Claude_Code_is_found_where_its_own_installer_puts_it()
    {
        var expected = Path.Combine(Home, ".local", "bin", "claude.exe");
        var probe = new FakeProbe { Files = { expected } };

        Claude().Locate(probe).Should().Be(AgentInstall.At(expected));
    }

    [Fact]
    public void An_agent_that_is_not_installed_reports_missing()
        => Claude().Locate(new FakeProbe()).Should().Be(AgentInstall.Missing);

    // The case that forced discovery to exist: the Store build is on no PATH at all.
    [Fact]
    public void Codex_is_found_through_the_path_its_own_config_declares()
    {
        var real = @"C:\Users\someone\AppData\Local\OpenAI\Codex\bin\abc123\codex.exe";
        var probe = new FakeProbe { Files = { real } };

        probe.Texts[Path.Combine(Home, ".codex", "config.toml")] =
            $"model = \"gpt-5.5\"\nCODEX_CLI_PATH = '{real}'\n";

        Codex().Locate(probe).Should().Be(AgentInstall.At(real));
    }

    // A declared path that no longer exists is not an answer — an upgrade can leave the key behind
    // pointing at a build that has been deleted.
    [Fact]
    public void A_declared_path_that_is_gone_falls_through_to_the_store_layout()
    {
        var build = Path.Combine(LocalAppData, "OpenAI", "Codex", "bin", "newest");
        var real = Path.Combine(build, "codex.exe");

        var probe = new FakeProbe { Files = { real } };

        probe.Texts[Path.Combine(Home, ".codex", "config.toml")] = "CODEX_CLI_PATH = 'C:\\gone\\codex.exe'\n";
        probe.Directories[Path.Combine(LocalAppData, "OpenAI", "Codex", "bin")] = [build];

        Codex().Locate(probe).Should().Be(AgentInstall.At(real));
    }

    [Fact]
    public void The_newest_store_build_wins()
    {
        var root = Path.Combine(LocalAppData, "OpenAI", "Codex", "bin");
        var newest = Path.Combine(root, "newest");
        var older = Path.Combine(root, "older");

        var probe = new FakeProbe
        {
            Files = { Path.Combine(newest, "codex.exe"), Path.Combine(older, "codex.exe") },
        };

        probe.Directories[root] = [newest, older];

        Codex().Locate(probe).ExplicitPath.Should().Be(Path.Combine(newest, "codex.exe"));
    }

    private static ClaudeCodeAdapter Claude()
        => new(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles());

    private static CodexAdapter Codex()
        => new(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles());

    private sealed class FakeProbe : IExecutableProbe
    {
        public HashSet<string> OnPathNames { get; } = [];

        public HashSet<string> Files { get; } = [];

        public Dictionary<string, IReadOnlyList<string>> Directories { get; } = [];

        public Dictionary<string, string> Texts { get; } = [];

        public string? OnPath(string executable)
            => OnPathNames.Contains(executable) ? $"/usr/bin/{executable}" : null;

        public string? FirstExisting(IEnumerable<string> candidates)
            => candidates.FirstOrDefault(Files.Contains);

        public IReadOnlyList<string> DirectoriesNewestFirst(string parent)
            => Directories.TryGetValue(parent, out var found) ? found : [];

        public string? ReadText(string path) => Texts.GetValueOrDefault(path);
    }
}
