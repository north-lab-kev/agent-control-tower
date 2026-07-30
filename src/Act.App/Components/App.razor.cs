using System.Globalization;

namespace Act.App.Components;

public partial class App(IAssetVersions assetVersions)
{
    private string? assetVersion;
    private string? radzenAssetVersion;

    private static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    private string AssetVersion
        => assetVersion ??= assetVersions.For(typeof(App).Assembly);

    private string RadzenAssetVersion
        => radzenAssetVersion ??= assetVersions.For(typeof(Radzen.Colors).Assembly);
}
