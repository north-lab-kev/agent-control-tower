using Microsoft.AspNetCore.Components;

namespace Act.App.Components;

public partial class App
{
    [Inject]
    private ICacheBuster CacheBuster { get; set; } = default!;

    private string AssetVersion => CacheBuster.Get(typeof(App).Assembly);
}
