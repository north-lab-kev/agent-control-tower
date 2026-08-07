using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

// One list decides two things — what `codex --image` is given and what the preview route will serve —
// so it is worth pinning that both read it the same way and that the set stays exactly this.
public class TaskAttachmentTests
{
    [Theory]
    [InlineData("shot.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("loop.gif", "image/gif")]
    [InlineData("modern.webp", "image/webp")]
    public void An_image_names_the_type_it_is_served_as(string fileName, string expected)
    {
        TaskAttachment.ImageContentTypeFor(fileName).Should().Be(expected);

        new TaskAttachment { FileName = fileName }.IsImage.Should().BeTrue();
    }

    // `.svg` is the one that matters here. It is an image everywhere else, and served from ACT's own
    // origin it is a script host — so the preview route has to refuse it, and `codex --image` never
    // sees it either. The rest are ordinary files an agent reads by path.
    [Theory]
    [InlineData("diagram.svg")]
    [InlineData("trace.log")]
    [InlineData("spec.pdf")]
    [InlineData("page.html")]
    [InlineData("notes")]
    [InlineData("archive.png.zip")]
    public void Everything_else_is_a_file_to_read_and_never_served(string fileName)
    {
        TaskAttachment.ImageContentTypeFor(fileName).Should().BeNull();

        new TaskAttachment { FileName = fileName }.IsImage.Should().BeFalse();
    }
}
