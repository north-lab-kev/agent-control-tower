using System.Globalization;

namespace Act.Core.Model;

public static class LanguagePreferenceExtensions
{
    private const string English = "en-CA";

    private const string French = "fr-CA";

    public static CultureInfo ToCulture(this LanguagePreference language, CultureInfo systemCulture) => language switch
    {
        LanguagePreference.English => CultureInfo.GetCultureInfo(English),
        LanguagePreference.French => CultureInfo.GetCultureInfo(French),
        _ => CultureInfo.GetCultureInfo(
            systemCulture.TwoLetterISOLanguageName == "fr" ? French : English),
    };
}
