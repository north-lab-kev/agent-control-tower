using Act.Core.Abstractions;
using Act.Infrastructure.FileSystem;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

// The port a usage dialect reads a credential file through. Every answer is "what is there", and a
// machine that never signed in to that CLI is the ordinary case rather than a failure.
public class TextFileReaderTests
{
    private readonly ITextFileReader reader = new TextFileReader();

    [Fact]
    public void An_existing_file_reads_back_verbatim()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var path = Path.Combine(temp.Path, "credentials.json");
        File.WriteAllText(path, "{\"token\":\"abc\"}");

        reader.Read(path).Should().Be("{\"token\":\"abc\"}");
    }

    [Fact]
    public void A_missing_file_is_null()
    {
        using var temp = new TempDirectory();

        reader.Read(Path.Combine(temp.Path, "credentials.json")).Should().BeNull();
    }

    [Fact]
    public void A_directory_is_null_rather_than_a_throw()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        reader.Read(temp.Path).Should().BeNull();
    }

    [Fact]
    public void An_empty_file_is_an_empty_string_not_a_miss()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var path = Path.Combine(temp.Path, "credentials.json");
        File.WriteAllText(path, string.Empty);

        reader.Read(path).Should().BeEmpty();
    }

    // A credential file the CLI still holds open is the normal case at startup, so a share-mode
    // clash must read as "nothing to see" rather than take the probe down.
    [Fact]
    public void A_file_locked_against_readers_is_null_rather_than_a_throw()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(temp.Path);

        var path = Path.Combine(temp.Path, "credentials.json");
        File.WriteAllText(path, "{}");

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var read = () => reader.Read(path);

        read.Should().NotThrow();
    }
}
