using Act.App.Components.Settings;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Radzen;

namespace Act.App.Components.Layout;

public partial class MainLayout(
    UserSettingsService settings,
    DialogService dialogService,
    ICardStore cards,
    IAssetVersions assetVersions) : IDisposable
{
    private string? markPath;

    private string ThemeAttribute => settings.Theme.ToString().ToLowerInvariant();

    private string MarkPath
        => markPath ??= $"favicon.png{assetVersions.For(typeof(MainLayout).Assembly)}";

    private BoardDensity Density => settings.Density;

    private int AttentionCount { get; set; }

    protected override async Task OnInitializedAsync()
    {
        settings.Changed += OnSettingsChanged;

        AttentionCount = (await cards.GetAllAsync()).Count(card => card.NeedsAttention);
    }

    public void Dispose() => settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OpenSettingsAsync()
        => await dialogService.OpenAsync<SettingsDialog>(
            Strings.Settings_Title,
            null,
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = true });
}
