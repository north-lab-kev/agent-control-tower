using System.Reflection;

namespace Act.App;

public interface IAssetVersions
{
    string For(Assembly assembly);
}

internal sealed class AssetVersions(IWebHostEnvironment environment) : IAssetVersions
{
    public string For(Assembly assembly)
    {
        var value = environment.IsDevelopment()
            ? Guid.NewGuid().ToString()
            : assembly.GetName().Version?.ToString() ?? "0";

        return $"?v={value}";
    }
}
