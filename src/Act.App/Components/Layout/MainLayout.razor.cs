using Act.App.Components.Settings;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Radzen;

namespace Act.App.Components.Layout;

public partial class MainLayout(UserSettingsService settings, DialogService dialogService) : IDisposable
{
    private string ThemeAttribute => settings.Theme.ToString().ToLowerInvariant();

    private BoardDensity Density => settings.Density;

    private static int AttentionCount => SampleBoard.Cards.Count(c => c.NeedsAttention);

    protected override void OnInitialized() => settings.Changed += OnSettingsChanged;

    public void Dispose() => settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OpenSettingsAsync()
        => await dialogService.OpenAsync<SettingsDialog>(
            Strings.Settings_Title,
            null,
            new DialogOptions { Width = "560px", CloseDialogOnOverlayClick = true });
}
