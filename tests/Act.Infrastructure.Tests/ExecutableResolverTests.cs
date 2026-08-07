using Act.Infrastructure.Terminal;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

[Collection(EnvironmentCollection.Name)]
public class ExecutableResolverTests
{
    [Fact]
    public void An_absolute_path_is_handed_back_untouched()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "agent.exe");

        ExecutableResolver.Resolve(rooted).Should().Be(rooted);
    }

    [Fact]
    public void A_bare_name_on_the_path_resolves_to_a_file_that_exists()
    {
        using var directory = new TempDirectory();

        Directory.CreateDirectory(directory.Path);

        var name = "act-fake-agent";
        var extension = OperatingSystem.IsWindows() ? ".cmd" : string.Empty;
        var executable = Path.Combine(directory.Path, name + extension);

        File.WriteAllText(executable, string.Empty);

        var original = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.Path + Path.PathSeparator + original);

            var resolved = ExecutableResolver.Resolve(name);

            // Case-insensitively, because the extension comes from `PATHEXT` (upper-case on
            // Windows) while the file on disk is lower-case, and Windows does not care.
            resolved.Should().BeEquivalentTo(executable);
            File.Exists(resolved).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    // The OS produces a better message than anything ACT could invent, and it names the binary
    // the user actually configured.
    [Fact]
    public void An_unresolvable_name_is_handed_back_for_the_OS_to_reject()
        => ExecutableResolver.Resolve("act-no-such-agent-anywhere").Should().Be("act-no-such-agent-anywhere");

    [Fact]
    public void A_malformed_path_entry_does_not_fail_the_lookup()
    {
        var original = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", "\"C:\\bad\0entry\"" + Path.PathSeparator + original);

            var resolve = () => ExecutableResolver.Resolve("act-no-such-agent-anywhere");

            resolve.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }
}
