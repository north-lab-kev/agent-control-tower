using System.Text;
using Act.Core.Model;
using Act.Infrastructure.FileSystem;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

// Real files in a temp directory, because every property here is a property of the filesystem: which
// characters a name may hold, whether a second file with the same name overwrites the first, and
// whether a delete of a directory the CLI still has open throws.
public class AttachmentStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 14, 30, 15, TimeSpan.Zero);

    private static readonly Guid CardId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public async Task A_saved_file_lands_under_the_cards_own_folder()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var attachment = await store.SaveAsync(CardId, "spec.md", Bytes("# spec"));

        attachment.FileName.Should().Be("spec.md");
        attachment.Length.Should().Be(Encoding.UTF8.GetByteCount("# spec"));
        attachment.AddedAt.Should().Be(Now);

        var path = store.PathFor(CardId, attachment);

        path.Should().Be(Path.Combine(temp.Path, AttachmentStore.DirectoryName, CardId.ToString("d"), "spec.md"));
        File.Exists(path).Should().BeTrue();
    }

    // The name comes from a browser, so it is never trusted with a path: a crafted one must not be
    // able to write outside the card's folder. Both separators and one fixed forbidden set on every
    // OS, so these cases assert the same thing on the Linux CI box as on a Windows desktop.
    [Theory]
    [InlineData(@"..\..\evil.txt", "evil.txt")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("a:b?c.txt", "a-b-c.txt")]
    [InlineData("..", null)]
    public async Task A_name_carrying_a_path_or_an_invalid_character_is_reduced_to_a_filename(
        string given,
        string? expected)
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var attachment = await store.SaveAsync(CardId, given, Bytes("x"));

        // Null means "nothing usable was left": a name made only of dots falls through to the same
        // timestamped answer a pasted bitmap gets.
        if (expected is null)
            attachment.FileName.Should().StartWith("pasted-");
        else
            attachment.FileName.Should().Be(expected);

        Path.GetFullPath(store.PathFor(CardId, attachment))
            .Should().StartWith(Path.GetFullPath(store.DirectoryFor(CardId)));
    }

    // Chromium names every bitmap pasted from the clipboard `image.png`, so two screenshots would
    // otherwise arrive as `image.png` and `image (2).png` with nothing to tell them apart.
    [Fact]
    public async Task A_pasted_bitmap_is_named_for_the_moment_it_arrived()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var attachment = await store.SaveAsync(CardId, "image.png", Bytes("png"));

        attachment.FileName.Should().StartWith("pasted-").And.EndWith(".png");
        attachment.IsImage.Should().BeTrue();
    }

    [Fact]
    public async Task Attaching_the_same_name_twice_keeps_both_files()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var first = await store.SaveAsync(CardId, "notes.txt", Bytes("one"));
        var second = await store.SaveAsync(CardId, "notes.txt", Bytes("two"));

        second.FileName.Should().Be("notes (2).txt");
        File.ReadAllText(store.PathFor(CardId, first)).Should().Be("one");
        File.ReadAllText(store.PathFor(CardId, second)).Should().Be("two");
    }

    // What makes remove-then-save and add-then-discard both end honest: the disk is brought back to
    // exactly the list the form committed.
    [Fact]
    public async Task Pruning_leaves_only_the_named_files()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var kept = await store.SaveAsync(CardId, "keep.txt", Bytes("keep"));
        var dropped = await store.SaveAsync(CardId, "drop.txt", Bytes("drop"));

        store.Prune(CardId, [kept.FileName]);

        File.Exists(store.PathFor(CardId, kept)).Should().BeTrue();
        File.Exists(store.PathFor(CardId, dropped)).Should().BeFalse();
    }

    // A duplicate owns its own files, so removing an attachment from one card cannot empty the
    // other's prompt.
    [Fact]
    public async Task A_copy_gets_its_own_files()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);
        var copyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var attachment = await store.SaveAsync(CardId, "spec.md", Bytes("# spec"));

        store.Copy(CardId, copyId);
        store.Clear(CardId);

        File.Exists(store.PathFor(copyId, attachment)).Should().BeTrue();
        File.Exists(store.PathFor(CardId, attachment)).Should().BeFalse();
    }

    // What the startup sweep reads: every folder that exists, so it can spot the ones no card claims.
    [Fact]
    public async Task Every_folder_is_reported_by_its_card_id()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        await store.SaveAsync(CardId, "spec.md", Bytes("# spec"));

        store.CardIds().Should().Equal([CardId]);

        store.Clear(CardId);

        store.CardIds().Should().BeEmpty();
    }

    [Fact]
    public void An_untouched_store_reports_no_folders()
    {
        using var temp = new TempDirectory();

        StoreIn(temp).CardIds().Should().BeEmpty();
    }

    // Created rather than only named, because both the `--add-dir` grant and a mid-session drop need
    // it to exist before there is anything in it.
    [Fact]
    public void The_folder_exists_as_soon_as_it_is_asked_for()
    {
        using var temp = new TempDirectory();

        Directory.Exists(StoreIn(temp).DirectoryFor(CardId)).Should().BeTrue();
    }

    // What the preview route stands on. The name it is handed comes off a url, so every case below is
    // an attack on the folder boundary rather than a happy-path variation.
    [Fact]
    public async Task A_file_the_card_holds_resolves_to_its_own_path()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);

        var attachment = await store.SaveAsync(CardId, "shot.png", Bytes("png"));

        store.ResolveInside(CardId, "shot.png").Should().Be(store.PathFor(CardId, attachment));
    }

    [Theory]
    [InlineData(@"..\..\act.db")]
    [InlineData("../../act.db")]
    [InlineData(@"..\..\..\..\..\Windows\win.ini")]
    [InlineData("subfolder/shot.png")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_that_climbs_out_of_the_folder_resolves_to_nothing(string name)
    {
        using var temp = new TempDirectory();

        StoreIn(temp).ResolveInside(CardId, name).Should().BeNull();
    }

    // `Path.Combine` discards everything before an absolute second argument, so a rooted name would
    // walk straight out of the folder if only the combine were trusted.
    [Fact]
    public void An_absolute_name_resolves_to_nothing()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);
        var outside = Path.Combine(temp.Path, "outside.png");

        // A real file to reach for, so the null below is the boundary refusing and not the file simply
        // being absent. `TempDirectory` names a path without creating it, hence the mkdir.
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(outside, "png");

        store.ResolveInside(CardId, outside).Should().BeNull();
    }

    // The reason the prefix test appends a separator: `…\<id>` is a string prefix of `…\<id>-evil`,
    // so a plain `StartsWith` would hand out a neighbouring card's files.
    [Fact]
    public void A_sibling_folder_sharing_the_ids_prefix_is_not_inside_it()
    {
        using var temp = new TempDirectory();
        var store = StoreIn(temp);
        var sibling = Path.Combine(temp.Path, AttachmentStore.DirectoryName, $"{CardId:d}-evil");

        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "shot.png"), "png");

        store.ResolveInside(CardId, @"..\" + $"{CardId:d}-evil" + @"\shot.png").Should().BeNull();
        store.ResolveInside(CardId, $"../{CardId:d}-evil/shot.png").Should().BeNull();
    }

    // A name inside the folder that simply is not there answers the same way a blocked one does, so
    // nothing can be probed for existence through this.
    [Fact]
    public void A_missing_file_resolves_to_nothing()
    {
        using var temp = new TempDirectory();

        StoreIn(temp).ResolveInside(CardId, "never-written.png").Should().BeNull();
    }

    private static AttachmentStore StoreIn(TempDirectory temp) => new(temp.Path, new FrozenClock(Now));

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    private sealed class FrozenClock(DateTimeOffset now) : Core.Abstractions.IClock
    {
        public DateTimeOffset Now => now;
    }
}
