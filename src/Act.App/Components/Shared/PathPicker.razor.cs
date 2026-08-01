using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Infrastructure.FileSystem;
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

    protected override void OnInitialized() => Open(NearestExisting(StartAt));

    private void Open(string? path) => listing = directories.List(path, PickingFiles);

    private Task ChooseAsync() => listing.Path is { } path ? OnPick.InvokeAsync(path) : Task.CompletedTask;

    private Task ChooseFileAsync(string path) => OnPick.InvokeAsync(path);

    private Task CancelAsync() => OnCancel.InvokeAsync();

    // In file mode the start value is itself a file, so the walk has to begin in the folder holding
    // it rather than at a path that is not a directory at all.
    private string? NearestExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return directories.Home;

        var check = directories.Check(path);
        if (!check.WellFormed)
            return directories.Home;

        var candidate = check.Resolved;

        if (PickingFiles && File.Exists(candidate))
            candidate = Directory.GetParent(candidate)?.FullName ?? string.Empty;

        while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
            candidate = Directory.GetParent(candidate)?.FullName ?? string.Empty;

        return string.IsNullOrEmpty(candidate) ? directories.Home : candidate;
    }

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
