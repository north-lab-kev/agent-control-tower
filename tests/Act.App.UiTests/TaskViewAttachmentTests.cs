using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;

namespace Act.App.UiTests;

// Files are written to disk on *add* rather than on save, which is what lets a 25 MB screenshot leave the
// server's memory immediately instead of being held per circuit until the user commits. The whole cost of
// that decision lands here: a discard has to undo the writes, a removal must not take the bytes until the
// save is real, and a locked card must never be pruned at all — that last one is the one plausible route by
// which once cost a launched task its attachment.
public class TaskViewAttachmentTests : ComponentTest
{
    // Refused whole rather than truncated. Keeping the first few of a dropped selection is the kind of
    // partial success nobody notices until the agent asks about a file that never came.
    [Fact]
    public void More_files_than_a_task_may_hold_is_refused_whole()
    {
        var cut = Show();

        Upload(cut, Enumerable.Range(0, TaskAttachment.MaxPerTask + 1).Select(i => $"file{i}.txt").ToArray());

        cut.FindAll("div.chips .chip").Should().BeEmpty();
        Notifications.Messages.Should().ContainSingle()
            .Which.Detail.Should().Contain(TaskAttachment.MaxPerTask.ToString());
    }

    [Fact]
    public void A_selection_that_would_overflow_what_is_already_there_is_refused_too()
    {
        var cut = Show();

        Upload(cut, [.. Enumerable.Range(0, TaskAttachment.MaxPerTask).Select(i => $"file{i}.txt")]);

        cut.FindAll("div.chips .chip").Should().HaveCount(TaskAttachment.MaxPerTask);

        Upload(cut, "one-more.txt");

        cut.FindAll("div.chips .chip").Should().HaveCount(TaskAttachment.MaxPerTask);
    }

    // Checked here as well as by `OpenReadStream`, so an oversized file is named in the message instead of
    // arriving as an `IOException` halfway through the copy — and the rest of the selection still lands.
    [Fact]
    public void An_oversized_file_is_refused_by_name_and_the_rest_still_arrives()
    {
        var cut = Show();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("ok", "small.txt"),
            Oversized("huge.png"));

        cut.FindAll("div.chips .chip .name").Select(chip => chip.TextContent).Should().Equal("small.txt");
        Notifications.Messages.Should().ContainSingle().Which.Detail.Should().Contain("huge.png");
    }

    [Fact]
    public void An_added_file_is_written_before_the_form_is_saved()
    {
        Upload(Show(), "notes.md");

        Attachments.CardIds().Should().ContainSingle();
    }

    // In memory only. The bytes go when the form commits, because a removal the user then abandons must not
    // leave the stored card naming a file that is gone.
    [Fact]
    public void Removing_a_chip_does_not_take_the_bytes_yet()
    {
        var cut = Show();

        Upload(cut, "notes.md");

        cut.Find("div.chip button:last-child").Click();

        cut.FindAll("div.chips .chip").Should().BeEmpty();
        Attachments.Pruned.Should().BeEmpty("nothing is reconciled until the save");
    }

    [Fact]
    public async Task Saving_prunes_to_the_names_that_survived()
    {
        var cut = Show();

        Upload(cut, "keep.md", "drop.md");

        cut.FindAll("div.chip")[1].QuerySelector("button:last-child")!.Click();

        cut.Find("div.titlerow input").Change("A task");
        cut.Find("textarea").Change("Do the thing");
        cut.Find("div.dirrow input").Change("/dev/act");

        await cut.Find("form").SubmitAsync();

        Attachments.Pruned.Should().ContainSingle().Which.Keep.Should().Equal("keep.md");
    }

    // Past the launch boundary the attachment list is immutable, so there is nothing to reconcile and
    // pruning is pure downside: it is a delete driven by the *form's* copy of the list against a card that
    // owns the real one.
    [Fact]
    public async Task A_locked_card_is_never_pruned()
    {
        var card = TaskViewTests.Card(BoardColumn.Executing);
        card.Attachments = [new TaskAttachment { FileName = "shot.png", Length = 2048 }];

        await Attachments.SaveAsync(card.Id, "shot.png", new MemoryStream([1, 2, 3]));
        await BoardWith(card);

        var cut = Render<TaskView>(p => p.Add(c => c.CardId, card.Id));

        cut.Find("div.titlerow input").Change("A new title");

        await cut.Find("form").SubmitAsync();

        Board.Card(card.Id)!.Title.Should().Be("A new title");
        Attachments.Pruned.Should().BeEmpty();
    }

    // The card keeps a *name*; the bytes are a separate thing that can go without it — a purge, a hand
    // reaching into the data directory. Asked at render time rather than stored, because a stored answer
    // would be a stale one.
    [Fact]
    public async Task A_file_whose_bytes_are_gone_says_so_instead_of_offering_to_open_it()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.Attachments = [new TaskAttachment { FileName = "shot.png", Length = 2048 }];

        await BoardWith(card);

        var chip = Render<TaskView>(p => p.Add(c => c.CardId, card.Id)).Find("div.chip button.open");

        chip.ClassList.Should().Contain("gone");
        chip.GetAttribute("title").Should().Contain("shot.png");
        chip.QuerySelector("i")!.TextContent.Should().Be("broken_image");
    }

    [Fact]
    public async Task A_file_that_is_there_offers_to_open_it()
    {
        var cut = await WithImage();

        var chip = cut.Find("div.chip button.open");

        chip.ClassList.Should().NotContain("gone");
        chip.QuerySelector("i")!.TextContent.Should().Be("image");
    }

    [Fact]
    public async Task Opening_a_file_hands_it_to_the_shell()
    {
        var cut = await WithImage();

        cut.Find("div.chip button.open").Click();

        Desktop.Opened.Should().ContainSingle().Which.Should().EndWith("shot.png");
    }

    [Fact]
    public async Task A_file_the_shell_refuses_is_reported()
    {
        Desktop.Refusal = "No application is associated with this file.";

        var cut = await WithImage();

        cut.Find("div.chip button.open").Click();

        Notifications.Messages.Should().ContainSingle()
            .Which.Detail.Should().Be("No application is associated with this file.");
    }

    // Only what can actually be shown. A log or a PDF has no thumbnail, and an empty frame under the cursor
    // reads as a broken image rather than as "nothing to preview".
    [Fact]
    public async Task Only_an_image_that_is_still_there_previews_on_hover()
    {
        var cut = await WithImage();

        cut.FindAll("div.preview").Should().BeEmpty();

        cut.Find("div.chip").MouseEnter();

        cut.Find("div.preview img").GetAttribute("src").Should().Contain("shot.png");

        cut.Find("div.chip").MouseLeave();

        cut.FindAll("div.preview").Should().BeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_an_image_never_previews()
    {
        var cut = Show();

        Upload(cut, "notes.md");

        cut.Find("div.chip").MouseEnter();

        cut.FindAll("div.preview").Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_whose_bytes_are_gone_never_previews()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.Attachments = [new TaskAttachment { FileName = "shot.png", Length = 2048 }];

        await BoardWith(card);

        var cut = Render<TaskView>(p => p.Add(c => c.CardId, card.Id));

        cut.Find("div.chip").MouseEnter();

        cut.FindAll("div.preview").Should().BeEmpty();
    }

    private async Task<IRenderedComponent<TaskView>> WithImage()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.Attachments = [new TaskAttachment { FileName = "shot.png", Length = 2048 }];

        await Attachments.SaveAsync(card.Id, "shot.png", new MemoryStream([1, 2, 3]));
        await BoardWith(card);

        return Render<TaskView>(p => p.Add(c => c.CardId, card.Id));
    }

    private static void Upload(IRenderedComponent<TaskView> cut, params string[] names)
        => cut.FindComponent<InputFile>().UploadFiles(
            [.. names.Select(name => InputFileContent.CreateFromText("contents", name))]);

    // Just over the ceiling, so the size check is what refuses it rather than the content. The bytes are
    // real, which is the only way bUnit reports a size — hence one byte over rather than 25 MB of zeroes.
    private static InputFileContent Oversized(string name)
        => InputFileContent.CreateFromBinary(new byte[TaskAttachment.MaxLength + 1], name);

    private IRenderedComponent<TaskView> Show() => Render<TaskView>();
}
