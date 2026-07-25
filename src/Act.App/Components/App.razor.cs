namespace Act.App.Components;

public partial class App(ICacheBuster cacheBuster)
{
    private string? assetVersion;
    private string? radzenAssetVersion;

    private string AssetVersion
        => assetVersion ??= cacheBuster.Get(typeof(App).Assembly);

    private string RadzenAssetVersion
        => radzenAssetVersion ??= cacheBuster.Get(typeof(Radzen.Colors).Assembly);
}
