using Act.Infrastructure.FileSystem;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class WorkingDirectoriesTests
{
    private readonly WorkingDirectories directories = new();

    [Fact]
    public void An_existing_directory_checks_out()
    {
        var check = directories.Check(Path.GetTempPath());

        check.WellFormed.Should().BeTrue();
        check.Exists.Should().BeTrue();
        check.Error.Should().BeNull();
    }

    // Missing is not malformed: you are allowed to save a task against a directory you intend to
    // create, which is why these are separate answers.
    [Fact]
    public void A_missing_directory_is_well_formed_but_absent()
    {
        var check = directories.Check(Path.Combine(Path.GetTempPath(), $"act-absent-{Guid.NewGuid():N}"));

        check.WellFormed.Should().BeTrue();
        check.Exists.Should().BeFalse();
        check.Error.Should().BeNull();
    }

    // Would otherwise resolve against whatever directory ACT was started in — a path the user
    // never named and would not recognise.
    [Fact]
    public void A_relative_path_is_rejected()
    {
        var check = directories.Check("dev/act");

        check.WellFormed.Should().BeFalse();
        check.Error.Should().Be(PathError.NotAbsolute);
    }

    [Fact]
    public void A_tilde_path_counts_as_absolute()
        => directories.Check("~/dev").WellFormed.Should().BeTrue();

    [Fact]
    public void An_empty_path_is_rejected()
        => directories.Check("   ").Error.Should().Be(PathError.Empty);

    [Fact]
    public void Listing_with_no_path_returns_the_roots()
    {
        var listing = directories.List(null);

        listing.Path.Should().BeNull();
        listing.Parent.Should().BeNull();
        listing.Directories.Should().NotBeEmpty("every OS has at least one root");
    }

    [Fact]
    public void Listing_a_directory_returns_its_sub_directories_and_a_parent()
    {
        using var temp = new TempDirectory();

        Directory.CreateDirectory(Path.Combine(temp.Path, "beta"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "alpha"));
        File.WriteAllText(Path.Combine(temp.Path, "ignored.txt"), string.Empty);

        var listing = directories.List(temp.Path);

        listing.Error.Should().BeNull();
        listing.Parent.Should().NotBeNull();
        listing.Directories.Select(entry => entry.Name).Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Listing_a_missing_directory_reports_rather_than_throws()
    {
        var listing = directories.List(Path.Combine(Path.GetTempPath(), $"act-absent-{Guid.NewGuid():N}"));

        listing.Error.Should().Be(PathError.Missing);
        listing.Directories.Should().BeEmpty();
    }

    // Whatever the OS calls its top, walking up from it has to stop rather than loop. On Windows
    // this is sharper than it looks: `C:` without its separator means "current directory on C",
    // so a careless trim walks *into* wherever the process was started.
    [Fact]
    public void A_root_has_no_parent()
    {
        foreach (var root in directories.List(null).Directories)
            directories.List(root.Path).Parent.Should().BeNull($"{root.Path} is a root");
    }

    // The other half of the same trap: a trailing separator makes `GetParent` return the directory
    // itself, so browsing up would silently stop moving.
    [Fact]
    public void A_trailing_separator_does_not_make_a_directory_its_own_parent()
    {
        using var temp = new TempDirectory();

        var child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);

        var listing = directories.List(child + Path.DirectorySeparatorChar);

        listing.Parent.Should().Be(Path.GetFullPath(temp.Path));
    }

    [Fact]
    public void Create_makes_the_directory_the_check_reported_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"act-create-{Guid.NewGuid():N}");

        try
        {
            directories.Check(path).Exists.Should().BeFalse();

            directories.Create(path);

            directories.Check(path).Exists.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
