using System.Collections.Concurrent;
using System.Reflection;

namespace Act.App;

public interface IAssetVersions
{
    string For(Assembly assembly);
}

// One token per assembly for the whole process. A fresh token each run still defeats the
// cache after a rebuild in development, but it must not change between the prerendered
// markup and the interactive re-render: a differing `href` makes Blazor patch the
// stylesheet links, and the browser repaints unstyled while it refetches them.
internal sealed class AssetVersions(IWebHostEnvironment environment) : IAssetVersions
{
    private readonly ConcurrentDictionary<Assembly, string> versions = new();

    public string For(Assembly assembly) => versions.GetOrAdd(assembly, Token);

    private string Token(Assembly assembly)
    {
        var value = environment.IsDevelopment()
            ? Guid.NewGuid().ToString()
            : assembly.GetName().Version?.ToString() ?? "0";

        return $"?v={value}";
    }
}
