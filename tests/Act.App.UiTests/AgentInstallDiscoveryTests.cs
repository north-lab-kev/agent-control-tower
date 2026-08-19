using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The startup pass that makes the common case need no configuration. Three outcomes, and the two
// rules that are easy to break: a path that still exists is never overwritten, and the switch is only
// ACT's to turn off once.
public class AgentInstallDiscoveryTests
{
    // Whether a recorded value is *a path* or *a bare name* is decided with the running platform's
    // separators, deliberately — the launch decides it the same way (see `AgentBinaryCheck`). So a
    // Windows-spelled path is a bare **name** on the Linux CI, and a test that hardcodes one asserts
    // the wrong branch there: it either fails outright or passes for the wrong reason. Spell every
    // path the way the platform running the test does, exactly as `AgentBinaryCheckTests` does.
    private static readonly string TypedPath =
        OperatingSystem.IsWindows() ? @"C:\my-build\claude.exe" : "/my-build/claude";

    private static readonly string RecordedPath =
        OperatingSystem.IsWindows() ? @"C:\tools\claude.exe" : "/tools/claude";

    private static readonly string RecordedCodexPath =
        OperatingSystem.IsWindows() ? @"C:\tools\codex.exe" : "/tools/codex";

    private static readonly string DeadPath =
        OperatingSystem.IsWindows() ? @"C:\gone\codex.exe" : "/gone/codex";

    // The Codex build-hash layout, which is the shape that made this rule necessary.
    private static readonly string OldBuild = OperatingSystem.IsWindows()
        ? @"C:\Users\me\AppData\Local\OpenAI\Codex\bin\OLDHASH\codex.exe"
        : "/home/me/.local/share/OpenAI/Codex/bin/OLDHASH/codex";

    private static readonly string NewBuild = OperatingSystem.IsWindows()
        ? @"C:\Users\me\AppData\Local\OpenAI\Codex\bin\NEWHASH\codex.exe"
        : "/home/me/.local/share/OpenAI/Codex/bin/NEWHASH/codex";

    private readonly FakeSettingsStore store = new();

    private readonly FakeExecutableProbe probe = new();

    // An empty setting *means* "resolve the bare name", and it keeps working when the install
    // upgrades itself out from under a recorded path. So a CLI on PATH is left alone.
    [Fact]
    public void An_agent_on_PATH_records_nothing_and_stays_enabled()
    {
        var settings = Run(AgentInstall.OnPath);

        var claude = settings.Defaults(AgentType.ClaudeCode);

        claude.Binary.Should().BeNullOrEmpty();
        claude.Enabled.Should().BeTrue();
    }

    // A pty spawns with an explicit image, so a CLI sitting on the disk but off PATH fails at spawn
    // with "not found" unless the path is written down.
    [Fact]
    public void An_agent_installed_off_PATH_gets_its_path_recorded()
    {
        var settings = Run(AgentInstall.At(RecordedPath));

        var claude = settings.Defaults(AgentType.ClaudeCode);

        claude.Binary.Should().Be(RecordedPath);
        claude.Enabled.Should().BeTrue();
    }

    [Fact]
    public void An_agent_that_is_not_installed_is_switched_off_on_the_first_pass()
    {
        var settings = Run(AgentInstall.Missing);

        var claude = settings.Defaults(AgentType.ClaudeCode);

        claude.Binary.Should().BeNullOrEmpty();
        claude.Enabled.Should().BeFalse();
    }

    [Fact]
    public void The_first_pass_is_recorded_so_it_only_happens_once()
    {
        var settings = Run(AgentInstall.Missing);

        settings.AgentInstallsProbed.Should().BeTrue();
    }

    // After the first pass the switch belongs to the user: someone who turns an agent back on is
    // saying they will install it, or that ACT is wrong about where it looks.
    [Fact]
    public void A_later_pass_leaves_an_agent_the_user_re_enabled_alone()
    {
        var settings = Settings();
        var discovery = Discovery(settings, AgentInstall.Missing);

        discovery.Run();
        settings.Defaults(AgentType.ClaudeCode).Enabled.Should().BeFalse();

        var reEnabled = settings.Defaults(AgentType.ClaudeCode);
        reEnabled.Enabled = true;
        settings.SetDefaults(reEnabled);

        discovery.Run();

        settings.Defaults(AgentType.ClaudeCode).Enabled.Should().BeTrue();
    }

    // The one answer that came from a human. Someone who points ACT at a specific build means it,
    // even when the probe finds a newer one somewhere else — as long as the build is still there.
    [Fact]
    public void A_path_the_user_typed_is_never_overwritten()
    {
        var settings = Settings();

        probe.Files.Add(TypedPath);

        var typed = settings.Defaults(AgentType.ClaudeCode);
        typed.Binary = TypedPath;
        settings.SetDefaults(typed);

        Discovery(settings, AgentInstall.At(RecordedPath)).Run();

        settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be(TypedPath);
    }

    [Fact]
    public void A_recorded_path_is_not_re_recorded_on_a_later_pass()
    {
        var settings = Settings();

        probe.Files.Add(RecordedPath);

        var discovery = Discovery(settings, AgentInstall.At(RecordedPath));

        discovery.Run();
        discovery.Run();

        settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be(RecordedPath);
    }

    // The Codex upgrade case, found live: the CLI installs under a build-hash directory, so the path
    // discovery recorded yesterday points at a folder that no longer exists — and because the setting
    // is no longer *empty*, the pass used to skip it and leave every launch failing at spawn.
    [Fact]
    public void A_recorded_path_that_the_install_moved_out_from_under_is_replaced()
    {
        var settings = Settings();

        var stale = settings.Defaults(AgentType.Codex);
        stale.Binary = OldBuild;
        settings.SetDefaults(stale);

        probe.Files.Add(NewBuild);

        new AgentInstallDiscovery(
            [new StubAdapter(AgentType.Codex, AgentInstall.At(NewBuild))],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance).Run();

        settings.Defaults(AgentType.Codex).Binary.Should().Be(NewBuild);
    }

    // Only when there is something to replace it with. A probe that found nothing must not blank a
    // path out — the recorded one is then still the best guess anybody has, and a CLI that is merely
    // mid-upgrade comes back.
    [Fact]
    public void A_dead_path_is_left_alone_when_discovery_finds_nothing()
    {
        var settings = Settings();

        var stale = settings.Defaults(AgentType.Codex);
        stale.Binary = DeadPath;
        settings.SetDefaults(stale);

        new AgentInstallDiscovery(
            [new StubAdapter(AgentType.Codex, AgentInstall.Missing)],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance).Run();

        settings.Defaults(AgentType.Codex).Binary.Should().Be(DeadPath);
    }

    // A bare name is not a path, and an empty-looking box is not the same thing as a name that stopped
    // resolving: "codex" means *resolve this on `PATH`*, and replacing it with an absolute path would
    // undo a choice the user made and pin them to one build.
    [Fact]
    public void A_bare_name_is_never_replaced_by_a_discovered_path()
    {
        var settings = Settings();

        var named = settings.Defaults(AgentType.Codex);
        named.Binary = "codex";
        settings.SetDefaults(named);

        new AgentInstallDiscovery(
            [new StubAdapter(AgentType.Codex, AgentInstall.At(RecordedCodexPath))],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance).Run();

        settings.Defaults(AgentType.Codex).Binary.Should().Be("codex");
    }

    [Fact]
    public void Every_registered_adapter_is_probed()
    {
        var settings = Settings();

        new AgentInstallDiscovery(
            [
                new StubAdapter(AgentType.ClaudeCode, AgentInstall.At(RecordedPath)),
                new StubAdapter(AgentType.Codex, AgentInstall.Missing),
            ],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance).Run();

        settings.Defaults(AgentType.ClaudeCode).Binary.Should().Be(RecordedPath);
        settings.Defaults(AgentType.ClaudeCode).Enabled.Should().BeTrue();
        settings.Defaults(AgentType.Codex).Enabled.Should().BeFalse();
    }

    // It runs before the board is usable, and the worst case without it is yesterday's
    // configuration — never a startup that stops.
    [Fact]
    public void An_adapter_that_throws_while_locating_does_not_stop_the_pass()
    {
        var settings = Settings();

        var discovery = new AgentInstallDiscovery(
            [
                new ThrowingAdapter(),
                new StubAdapter(AgentType.Codex, AgentInstall.At(RecordedCodexPath)),
            ],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance);

        var run = () => discovery.Run();

        run.Should().NotThrow();
        settings.Defaults(AgentType.Codex).Binary.Should().Be(RecordedCodexPath);
    }

    // A failure is not a "not installed": switching the agent off would be acting on an answer the
    // probe never gave.
    [Fact]
    public void An_adapter_that_throws_is_left_enabled_with_nothing_recorded()
    {
        var settings = Settings();

        new AgentInstallDiscovery(
            [new ThrowingAdapter()],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance).Run();

        var claude = settings.Defaults(AgentType.ClaudeCode);

        claude.Enabled.Should().BeTrue();
        claude.Binary.Should().BeNullOrEmpty();
    }

    private UserSettingsService Run(AgentInstall install)
    {
        var settings = Settings();

        Discovery(settings, install).Run();

        return settings;
    }

    private UserSettingsService Settings() => new(store, new AppCulture());

    private AgentInstallDiscovery Discovery(UserSettingsService settings, AgentInstall install)
        => new(
            [new StubAdapter(AgentType.ClaudeCode, install)],
            probe,
            settings,
            NullLogger<AgentInstallDiscovery>.Instance);

    private class StubAdapter(AgentType agent, AgentInstall install) : IAgentAdapter
    {
        public AgentType Agent => agent;

        public AgentCapabilities Capabilities => new([], null, []);

        public LaunchConfigResolution Resolve(LaunchConfig config) => new(config, [], []);

        public virtual AgentInstall Locate(IExecutableProbe probe) => install;

        public string? DesktopHandoffUrl(string sessionId, string workingDir) => null;

        public Task<IAgentSession> LaunchAsync(AgentLaunchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IAgentSession> ResumeAsync(AgentResumeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> QueryAsync(AgentQueryRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingAdapter() : StubAdapter(AgentType.ClaudeCode, AgentInstall.Missing)
    {
        public override AgentInstall Locate(IExecutableProbe probe)
            => throw new UnauthorizedAccessException("the probe walked into a folder it may not read");
    }
}
