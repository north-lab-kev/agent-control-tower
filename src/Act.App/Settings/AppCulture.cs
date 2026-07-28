using System.Globalization;
using Act.Core.Model;

namespace Act.App.Settings;

public sealed class AppCulture
{
    private readonly CultureInfo systemCulture = CultureInfo.CurrentUICulture;

    public void Apply(LanguagePreference language)
    {
        var culture = language.ToCulture(systemCulture);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
