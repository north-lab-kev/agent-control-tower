using Act.Core.Abstractions;
using Act.Infrastructure.Terminal;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

// In the environment collection because `OnPath` reads the process-wide `PATH`.
[Collection(EnvironmentCollection.Name)]
public class ExecutableProbeTests
{
    private readonly IExecutableProbe probe = new ExecutableProbe();

    // A binary this test plants itself rather than one the machine happens to have: the hit case
    // then says something on a build agent with no agent CLI installed, and it does not depend on
    // what `PATH` held when the suite started.
    [Fact]
    public void An_executable_on_PATH_resolves_to_a_full_path()
    {
        using var directory = new TempDirectory();

        Directory.CreateDirectory(directory.Path);

        var name = "act-fake-probe-target";
        var extension = OperatingSystem.IsWindows() ? ".cmd" : string.Empty;
        var planted = Path.Combine(directory.Path, name + extension);

        File.WriteAllText(planted, string.Empty);

        var original = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.Path + Path.PathSeparator + original);

            var found = probe.OnPath(name);

            found.Should().NotBeNull();
            found.Should().NotBe(name, "an unchanged answer is how the resolver reports a miss");
            found.Should().BeEquivalentTo(planted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    [Fact]
    public void An_executable_that_is_not_on_PATH_is_null_rather_than_the_name_back()
        => probe.OnPath($"act-absent-{Guid.NewGuid():N}").Should().BeNull();

    [Fact]
    public void The_first_candidate_that_exists_wins()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var second = Path.Combine(temp.Path, "second");
        var third = Path.Combine(temp.Path, "third");

        File.WriteAllText(second, string.Empty);
        File.WriteAllText(third, string.Empty);

        probe.FirstExisting([Path.Combine(temp.Path, "first"), second, third]).Should().Be(second);
    }

    [Fact]
    public void No_candidate_existing_is_null()
    {
        using var temp = new TempDirectory();

        probe.FirstExisting([Path.Combine(temp.Path, "first")]).Should().BeNull();
    }

    [Fact]
    public void An_empty_candidate_list_is_null()
        => probe.FirstExisting([]).Should().BeNull();

    // A blank entry is what an unset setting looks like by the time it reaches here, and a path
    // with a null byte in it throws from `File.Exists` on some runtimes. Neither may stop a probe.
    [Fact]
    public void A_blank_or_malformed_candidate_is_skipped_rather_than_thrown_on()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var real = Path.Combine(temp.Path, "real");
        File.WriteAllText(real, string.Empty);

        probe.FirstExisting(["", "   ", "\0invalid", real]).Should().Be(real);
    }

    [Fact]
    public void Directories_come_back_newest_first()
    {
        using var temp = new TempDirectory();

        var older = Directory.CreateDirectory(Path.Combine(temp.Path, "older"));
        var newer = Directory.CreateDirectory(Path.Combine(temp.Path, "newer"));

        older.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-2);
        newer.LastWriteTimeUtc = DateTime.UtcNow;

        probe.DirectoriesNewestFirst(temp.Path).Should().Equal(newer.FullName, older.FullName);
    }

    [Fact]
    public void Files_beside_the_directories_are_not_listed()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(Path.Combine(temp.Path, "version"));
        File.WriteAllText(Path.Combine(temp.Path, "readme.txt"), string.Empty);

        probe.DirectoriesNewestFirst(temp.Path)
            .Should().ContainSingle().Which.Should().EndWith("version");
    }

    // The normal answer on a machine where the agent is not installed at all.
    [Fact]
    public void A_missing_parent_lists_empty_rather_than_throwing()
        => probe.DirectoriesNewestFirst(Path.Combine(Path.GetTempPath(), $"act-absent-{Guid.NewGuid():N}"))
            .Should().BeEmpty();

    [Fact]
    public void A_malformed_parent_lists_empty_rather_than_throwing()
        => probe.DirectoriesNewestFirst("\0invalid").Should().BeEmpty();

    [Fact]
    public void Reading_a_file_gives_its_text()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var path = Path.Combine(temp.Path, "version.txt");
        File.WriteAllText(path, "2.1.0");

        probe.ReadText(path).Should().Be("2.1.0");
    }

    [Fact]
    public void Reading_a_missing_file_is_null()
    {
        using var temp = new TempDirectory();

        probe.ReadText(Path.Combine(temp.Path, "version.txt")).Should().BeNull();
    }

    [Fact]
    public void Reading_a_directory_is_null_rather_than_a_throw()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        probe.ReadText(temp.Path).Should().BeNull();
    }

    // A probe runs at startup against a `config.toml` the CLI may be holding open. Asserted as "does
    // not throw" rather than as an outcome because only Windows really enforces the share mode.
    [Fact]
    public void Reading_a_file_that_is_held_open_does_not_stop_the_probe()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var path = Path.Combine(temp.Path, "config.toml");
        File.WriteAllText(path, "CODEX_CLI_PATH = 'x'");

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var read = () => probe.ReadText(path);

        read.Should().NotThrow();
    }
}
