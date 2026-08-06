using Act.App.Components.Shared;
using Act.Core.Abstractions;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The picker browses the *server's* filesystem, which in ACT is the user's own machine, and every path
// answer is `IWorkingDirectories`' rather than its own — that is what stops it drifting from the launch
// and the form. So what a test can hold it to is the walk: where it opens, what a click does, and the
// one real difference between its two modes — a directory is confirmed where you are standing, a file
// is chosen the moment you click it.
public class PathPickerTests : ComponentTest
{
    [Fact]
    public void It_opens_where_the_port_says_to_start()
    {
        Directories.With("/dev/act", "src", "docs");

        var cut = Picker("/dev/act/Program.cs");

        cut.Find("span.here").TextContent.Should().Be("/dev/act");
        cut.FindAll("button.entry").Select(Name).Should().Equal("src", "docs");
    }

    // A blank start is the roots, not the home directory: on Windows that is the drive list, and it is
    // the only place a walk can begin when nothing has been typed.
    [Fact]
    public void With_nothing_to_start_from_it_opens_at_home()
    {
        Directories.Home = "/home/act";
        Directories.With("/home/act", "dev");

        Picker(null).Find("span.here").TextContent.Should().Be("/home/act");
    }

    [Fact]
    public void Clicking_a_folder_walks_into_it()
    {
        Directories.With("/dev/act", "src").With("/dev/act/src", "Act.Core");

        var cut = Picker("/dev/act");

        cut.Find("button.entry").Click();

        cut.Find("span.here").TextContent.Should().Be("/dev/act/src");
        Name(cut.Find("button.entry")).Should().Be("Act.Core");
    }

    [Fact]
    public void The_up_arrow_is_dead_at_a_root()
    {
        Directories.With("/dev", "act");

        Picker("/dev").FindAll("div.crumbs button")[2].HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void The_up_arrow_walks_to_the_parent()
    {
        Directories.With("/dev/act", "src").With("/dev", "act");

        var cut = Picker("/dev/act");

        cut.FindAll("div.crumbs button")[2].Click();

        cut.Find("span.here").TextContent.Should().Be("/dev");
    }

    // The answer is where you are standing, so it is confirmed rather than clicked — and the confirm is
    // dead at the roots, where "here" is not a directory at all.
    [Fact]
    public void Picking_a_directory_ends_with_the_folder_you_are_standing_in()
    {
        Directories.With("/dev/act", "src");

        var picked = new List<string>();
        var cut = Picker("/dev/act", picked);

        cut.Find("div.pickeractions button:last-child").Click();

        picked.Should().Equal("/dev/act");
    }

    [Fact]
    public void There_is_nothing_to_confirm_at_the_roots()
    {
        Directories.Home = "/home/act";
        Directories.With("/home/act", "dev").With(null, "C:");

        var cut = Picker("/home/act");

        cut.FindAll("div.crumbs button")[1].Click();

        cut.Find("span.here").TextContent.Should().NotBe("/home/act");
        cut.Find("div.pickeractions button:last-child").HasAttribute("disabled").Should().BeTrue();
    }

    // File mode has no confirm at all: the answer is a thing in the list, and a second way to say the
    // same thing is a second thing that can disagree.
    [Fact]
    public void File_mode_offers_no_confirm_button()
    {
        Directories.WithFiles("/usr/bin", ["local"], "claude");

        Picker("/usr/bin", mode: PathPickerMode.File)
            .FindAll("div.pickeractions button").Should().ContainSingle();
    }

    [Fact]
    public void Clicking_a_file_is_the_choice()
    {
        Directories.WithFiles("/usr/bin", ["local"], "claude");

        var picked = new List<string>();
        var cut = Picker("/usr/bin", picked, mode: PathPickerMode.File);

        cut.Find("button.entry.file").Click();

        picked.Should().Equal("/usr/bin/claude");
    }

    // Folders first, then files: walking is the frequent action and stays where it was before files
    // existed in this list at all.
    [Fact]
    public void Folders_come_before_files()
    {
        Directories.WithFiles("/usr/bin", ["local"], "claude");

        var entries = Picker("/usr/bin", mode: PathPickerMode.File).FindAll("button.entry");

        entries[0].ClassList.Should().NotContain("file");
        entries[1].ClassList.Should().Contain("file");
    }

    [Fact]
    public void An_empty_folder_says_so()
    {
        Directories.With("/dev/act");

        var cut = Picker("/dev/act");

        cut.FindAll("button.entry").Should().BeEmpty();
        cut.FindAll(".pickerhint").Should().ContainSingle();
    }

    // Each refusal gets its own wording, because "missing" and "unreadable" are different problems with
    // different fixes and the picker is where the user finds out which one they have.
    [Theory]
    [InlineData(PathError.Missing)]
    [InlineData(PathError.Unreadable)]
    [InlineData(PathError.Malformed)]
    public void A_folder_that_will_not_open_says_which_way(string error)
    {
        Directories.WithError("/dev/act", error);

        var cut = Picker("/dev/act");

        cut.FindAll("button.entry").Should().BeEmpty();
        cut.Find(".pickererr").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Each_refusal_is_worded_differently()
    {
        Directories.WithError("/a", PathError.Missing).WithError("/b", PathError.Unreadable);

        Picker("/a").Find(".pickererr").TextContent
            .Should().NotBe(Picker("/b").Find(".pickererr").TextContent);
    }

    [Fact]
    public void Cancel_hands_back_nothing_but_the_cancel()
    {
        Directories.With("/dev/act", "src");

        var cancelled = 0;

        Render<PathPicker>(p => p
                .Add(c => c.StartAt, "/dev/act")
                .Add(c => c.OnPick, _ => { })
                .Add(c => c.OnCancel, () => cancelled++))
            .Find("div.pickeractions button:first-child").Click();

        cancelled.Should().Be(1);
    }

    // The entry's own text, past the icon ligature `RadzenIcon` renders beside it.
    private static string Name(AngleSharp.Dom.IElement entry) => entry.QuerySelector("span")!.TextContent.Trim();

    private IRenderedComponent<PathPicker> Picker(
        string? start,
        List<string>? picked = null,
        PathPickerMode mode = PathPickerMode.Directory)
        => Render<PathPicker>(p => p
            .Add(c => c.StartAt, start)
            .Add(c => c.Mode, mode)
            .Add(c => c.OnPick, path => (picked ?? []).Add(path))
            .Add(c => c.OnCancel, () => { }));
}
