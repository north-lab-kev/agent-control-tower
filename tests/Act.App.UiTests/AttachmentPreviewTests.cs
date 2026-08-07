using Act.App.Components.Shared;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Two contracts, both invisible in the component itself and both easy to break. `act-attach.place`
// finds this box by the `Id` it was handed, so the id has to be on the outer element rather than on
// the image — and the box is positioned *after* the render that added it, so it has to arrive hidden
// or it paints once at the viewport's corner on the way to where it belongs.
public class AttachmentPreviewTests : ComponentTest
{
    [Fact]
    public void The_box_wears_the_id_the_placer_looks_up()
    {
        var cut = Render<AttachmentPreview>(p => p
            .Add(c => c.Id, "act-preview-1")
            .Add(c => c.Url, "/attachments/a/shot.png"));

        cut.Find("div.preview").Id.Should().Be("act-preview-1");
        cut.Find("div.preview img").HasAttribute("id")
            .Should().BeFalse("the placer measures the box, not the image");
    }

    [Fact]
    public void The_image_is_the_url_it_was_given()
    {
        var image = Render<AttachmentPreview>(p => p
                .Add(c => c.Id, "act-preview-1")
                .Add(c => c.Url, "/attachments/a/shot.png")
                .Add(c => c.Alt, "shot.png"))
            .Find("img");

        image.GetAttribute("src").Should().Be("/attachments/a/shot.png");
        image.GetAttribute("alt").Should().Be("shot.png");
    }

    // The css owns the hiding — `visibility: hidden` on `.preview` until `place` lands — so all this
    // asserts is that the class carrying it is the one the box actually renders with.
    [Fact]
    public void The_box_is_only_ever_the_class_the_placer_and_the_css_agree_on()
    {
        Render<AttachmentPreview>(p => p
                .Add(c => c.Id, "act-preview-1")
                .Add(c => c.Url, "/attachments/a/shot.png"))
            .Find("div").ClassName.Should().Be("preview");
    }
}
