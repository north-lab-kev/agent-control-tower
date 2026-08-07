using Act.App.Resources;
using Act.Core.Abstractions;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Shared;

// Browses the *server's* filesystem, which in ACT is the user's own machine. That is what makes
// one picker work everywhere: a browser cannot hand back an absolute path (the File System Access
// API deliberately withholds it) and a native dialog only exists under the Electron shell, so
// neither works in both modes. Walking directories over the circuit works in both, and on Windows
// and Linux alike — the roots are drives or `/`, and the port answers which.
//
// Two modes, one walk. Picking a **directory** ends with *Use this folder*, because the answer is
// where you are standing. Picking a **file** ends the moment you click one, because the answer is
// a thing in the list and there is nothing further to confirm.
public partial class PathPicker(IWorkingDirectories directories)
{
    private DirectoryListing listing = new(null, null, []);

    [Parameter, EditorRequired]
    public EventCallback<string> OnPick { get; set; }

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    // Where to open. A path that does not exist yet still gives a useful starting point: its
    // nearest existing ancestor, so typing most of a path then browsing lands nearby.
    [Parameter]
    public string? StartAt { get; set; }

    [Parameter]
    public PathPickerMode Mode { get; set; } = PathPickerMode.Directory;

    private bool PickingFiles => Mode is PathPickerMode.File;

    // Where to start is the port's answer, not this component's: resolving a path, knowing what a
    // root is on this OS and walking to the nearest real folder are all things `IWorkingDirectories`
    // already does for the launch and the form, and a picker that worked it out with its own
    // `Directory` calls could only drift from them.
    protected override void OnInitialized() => Open(directories.NearestDirectory(StartAt));

    private void Open(string? path) => listing = directories.List(path, PickingFiles);

    private Task ChooseAsync() => listing.Path is { } path ? OnPick.InvokeAsync(path) : Task.CompletedTask;

    private Task ChooseFileAsync(string path) => OnPick.InvokeAsync(path);

    private Task CancelAsync() => OnCancel.InvokeAsync();

    private static string ErrorText(string error) => error switch
    {
        PathError.Missing => Strings.Picker_Missing,
        PathError.Unreadable => Strings.Picker_Unreadable,
        _ => Strings.Picker_Malformed,
    };
}

public enum PathPickerMode
{
    Directory,
    File,
}
