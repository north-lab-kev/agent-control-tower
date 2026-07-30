using Act.App.Resources;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Shared;

// The one prompt in the app that is a real dialog. Everything else became a page because it was a
// place you go; this is a fork in an action already underway, and it decides the fate of tasks
// other than the one on screen — too much to put in a footer strip.
public partial class ArchiveFollowUpsDialog(DialogService dialogService)
{
    [Parameter, EditorRequired]
    public Card Card { get; set; } = default!;

    [Parameter, EditorRequired]
    public IReadOnlyList<Card> FollowUps { get; set; } = [];

    private string Question => Text.Format(Strings.Task_DeleteFollowUps, FollowUps.Count);

    private void Close(FollowUpChoice choice) => dialogService.Close(choice);
}

public enum FollowUpChoice
{
    Cancel,
    KeepFollowUps,
    WithFollowUps,
}
