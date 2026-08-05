using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Shared;

// No logic of its own: the box is markup and css, and *where* it goes is JS's answer because a fixed
// element needs viewport coordinates. The caller renders it inside the row being hovered and calls
// `act-attach.place` with the same `Id` once it is in the document.
public partial class AttachmentPreview
{
    [Parameter]
    [EditorRequired]
    public string Id { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public string Url { get; set; } = string.Empty;

    [Parameter]
    public string? Alt { get; set; }
}
