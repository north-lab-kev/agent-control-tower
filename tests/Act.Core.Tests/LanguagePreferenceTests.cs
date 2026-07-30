using System.Globalization;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class LanguagePreferenceTests
{
    [Fact]
    public void English_maps_to_canadian_english()
        => LanguagePreference.English.ToCulture(Culture("fr-FR")).Name.Should().Be("en-CA");

    [Fact]
    public void French_maps_to_canadian_french()
        => LanguagePreference.French.ToCulture(Culture("en-US")).Name.Should().Be("fr-CA");

    [Theory]
    [InlineData("fr-CA")]
    [InlineData("fr-FR")]
    [InlineData("fr")]
    public void System_follows_a_french_system_culture(string systemCulture)
        => LanguagePreference.System.ToCulture(Culture(systemCulture)).Name.Should().Be("fr-CA");

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void System_falls_back_to_english_for_any_other_system_culture(string systemCulture)
        => LanguagePreference.System.ToCulture(Culture(systemCulture)).Name.Should().Be("en-CA");

    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);
}
