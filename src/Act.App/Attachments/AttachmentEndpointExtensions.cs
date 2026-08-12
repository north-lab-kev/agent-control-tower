using Act.Core.Model;
using Act.Infrastructure.FileSystem;

namespace Act.App.Attachments;

// The one route the browser is given into ACT's data directory: a task's attached image, so a chip
// can show a thumbnail on hover. It exists because attachments deliberately live outside `wwwroot`
// (see *Attachments* in the spec) and an `<img>` cannot read a path.
//
// Thin on purpose, like `Act.App/Hooks/`: everything worth testing is elsewhere. Which extensions
// are images is `TaskAttachment`'s answer, and whether a name resolves inside the card's own folder
// is `IAttachmentStore.ResolveInside`'s — both unit-tested without a web host. What is left here is
// the status code and the header.
//
// It needs no port guard of its own: the path is not a hook path, so `HookPortGuard` already serves
// it only off the hook port, and a GET is not something `UseAntiforgery` gates.
public static class AttachmentEndpointExtensions
{
    public const string RoutePrefix = "/attachments";

    // Composed here rather than at each call site, so the route and the url that hits it cannot drift.
    // The file name is escaped rather than trusted to be url-safe: `AttachmentStore` allows spaces and
    // anything else the platform permits, and the segment has to survive them. The endpoint re-checks
    // the decoded name against the card's own folder either way.
    public static string UrlFor(Guid cardId, TaskAttachment attachment)
        => $"{RoutePrefix}/{cardId:d}/{Uri.EscapeDataString(attachment.FileName)}";

    public static void MapActAttachments(this IEndpointRouteBuilder routes)
        => routes.MapGet(
                $"{RoutePrefix}/{{cardId:guid}}/{{fileName}}",
                (HttpContext context, Guid cardId, string fileName, IAttachmentStore attachments) =>
                {
                    // Images only, and the type is taken from the name rather than sniffed from the
                    // bytes: this route hands local files to ACT's own origin, so what it will serve
                    // has to be a short list decided in advance, not whatever a file turns out to be.
                    if (TaskAttachment.ImageContentTypeFor(fileName) is not { } contentType)
                        return Results.NotFound();

                    if (attachments.ResolveInside(cardId, fileName) is not { } path)
                        return Results.NotFound();

                    // Belt and braces with the fixed content type above: even a correct declaration is
                    // worth pinning, because a sniffing browser is what turns a mislabelled file into
                    // an executable one.
                    context.Response.Headers.XContentTypeOptions = "nosniff";

                    return Results.File(path, contentType, enableRangeProcessing: false);
                })
            .WithName("act-attachment-preview");
}
