using System.Reflection;

namespace Act.App;

public interface ICacheBuster
{
    string Get(Assembly assembly);
}

internal sealed class CacheBuster(IWebHostEnvironment environment) : ICacheBuster
{
    public string Get(Assembly assembly)
    {
        var value = environment.IsDevelopment()
            ? Guid.NewGuid().ToString()
            : assembly.GetName().Version?.ToString() ?? "0";

        return $"?v={value}";
    }
}
