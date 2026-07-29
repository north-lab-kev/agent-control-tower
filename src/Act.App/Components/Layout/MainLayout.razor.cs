using Act.App.Cards;
using Act.App.Components.Board;
using Act.App.Components.Settings;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Microsoft.JSInterop;
using Radzen;

namespace Act.App.Components.Layout;

public partial class MainLayout(
    UserSettingsService settings,
    DialogService dialogService,
    BoardState board,
    IAssetVersions assetVersions,
    IJSRuntime js) : IDisposable
{
    private string? markPath;

    private ThemePreference appliedTheme = settings.Theme;

    private string ThemeAttribute => settings.Theme.ToString().ToLowerInvariant();

    private BoardDensity Density => settings.Density;

    private int AttentionCount => board.AttentionCount;

    private string MarkPath
        => markPath ??= $"favicon.png{assetVersions.For(typeof(MainLayout).Assembly)}";

    protected override void OnInitialized()
    {
        settings.Changed += OnChanged;
        board.Changed += OnChanged;
    }

    public void Dispose()
    {
        settings.Changed -= OnChanged;
        board.Changed -= OnChanged;
    }

    private void OnChanged() => _ = InvokeAsync(async () =>
    {
        StateHasChanged();

        if (appliedTheme == settings.Theme)
            return;

        appliedTheme = settings.Theme;

        await js.InvokeVoidAsync(
            "actTheme.apply",
            ThemeMedia.LightId,
            ThemeMedia.Light(appliedTheme),
            ThemeMedia.DarkId,
            ThemeMedia.Dark(appliedTheme));
    });

    private async Task OpenNewTaskAsync()
        => await dialogService.OpenAsync<TaskDialog>(
            Strings.NewTask_DialogTitle,
            null,
            new DialogOptions
            {
                Width = "640px",
                CloseDialogOnOverlayClick = false,
                CssClass = "act-dialog act-dialog-form",
            });

    private async Task OpenSettingsAsync()
        => await dialogService.OpenAsync<SettingsDialog>(
            Strings.Settings_Title,
            null,
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = true, CssClass = "act-dialog" });
}
