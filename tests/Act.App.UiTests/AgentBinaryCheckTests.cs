using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// An empty box means "resolve the bare name on PATH", so what it reports has to be about `PATH` and
// nothing else. The first version asked the adapter whether the CLI was *found anywhere* and said
// "Found on PATH" when it was not — which is exactly the reassurance that hides a launch that will
// fail with "not found" for a CLI sitting on the disk.
public class AgentBinaryCheckTests
{
    [Fact]
    public void An_empty_box_is_only_on_PATH_when_the_name_really_resolves()
        => AgentBinaryCheck.For(new Probe(), Adapter(AgentInstall.OnPath), string.Empty)
            .Should().Be(new AgentBinaryState(AgentBinaryStatus.OnPath));

    [Fact]
    public void An_empty_box_warns_when_the_agent_is_installed_off_PATH()
    {
        var state = AgentBinaryCheck.For(
            new Probe(),
            Adapter(AgentInstall.At(@"C:\tools\codex.exe")),
            string.Empty);

        state.Status.Should().Be(AgentBinaryStatus.NotOnPath);
        state.DiscoveredPath.Should().Be(@"C:\tools\codex.exe");
    }

    [Fact]
    public void An_empty_box_with_nothing_installed_says_so()
        => AgentBinaryCheck.For(new Probe(), Adapter(AgentInstall.Missing), string.Empty)
            .Status.Should().Be(AgentBinaryStatus.NotInstalled);

    [Fact]
    public void A_typed_path_that_exists_is_found()
        => AgentBinaryCheck.For(
                new Probe { Files = { @"C:\tools\codex.exe" } },
                Adapter(AgentInstall.Missing),
                @"C:\tools\codex.exe")
            .Status.Should().Be(AgentBinaryStatus.Found);

    [Fact]
    public void A_typed_path_that_does_not_exist_is_not_found()
        => AgentBinaryCheck.For(new Probe(), Adapter(AgentInstall.OnPath), @"C:\nope\codex.exe")
            .Status.Should().Be(AgentBinaryStatus.NotFound);

    // A bare name is resolved the way the launch resolves it, not treated as a path that is missing.
    [Fact]
    public void A_typed_bare_name_is_resolved_on_PATH()
        => AgentBinaryCheck.For(new Probe { OnPathNames = { "codex" } }, Adapter(AgentInstall.Missing), "codex")
            .Status.Should().Be(AgentBinaryStatus.Found);

    [Fact]
    public void A_typed_bare_name_that_does_not_resolve_is_not_found()
        => AgentBinaryCheck.For(new Probe(), Adapter(AgentInstall.OnPath), "nosuchcli")
            .Status.Should().Be(AgentBinaryStatus.NotFound);

    private static IAgentAdapter Adapter(AgentInstall install) => new StubAdapter(install);

    private sealed class StubAdapter(AgentInstall install) : IAgentAdapter
    {
        public AgentType Agent => AgentType.Codex;

        public AgentCapabilities Capabilities => new([], null, []);

        public LaunchConfigResolution Resolve(LaunchConfig config) => new(config, [], []);

        public AgentInstall Locate(IExecutableProbe probe) => install;

        public string? DesktopHandoffUrl(string sessionId, string workingDir) => null;

        public Task<IAgentSession> LaunchAsync(AgentLaunchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IAgentSession> ResumeAsync(AgentResumeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> QueryAsync(AgentQueryRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Probe : IExecutableProbe
    {
        public HashSet<string> OnPathNames { get; } = [];

        public HashSet<string> Files { get; } = [];

        public string? OnPath(string executable)
            => OnPathNames.Contains(executable) ? $"/usr/bin/{executable}" : null;

        public string? FirstExisting(IEnumerable<string> candidates) => candidates.FirstOrDefault(Files.Contains);

        public IReadOnlyList<string> DirectoriesNewestFirst(string parent) => [];

        public string? ReadText(string path) => null;
    }
}
