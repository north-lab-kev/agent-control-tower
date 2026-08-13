using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Act.App.UiTests;

// A missing key in a satellite resource does not throw — `ResourceManager` falls back to the neutral
// one — so a French user silently reads English and nothing fails. Every gap this test now catches got
// in exactly that way: strings were added to `Strings.resx` and the translation was forgotten, and
// retired keys stayed behind in `Strings.fr.resx` after the feature that used them was gone.
public class StringResourceParityTests
{
    [Fact]
    public void French_translates_every_string_and_no_retired_ones()
    {
        var neutral = Keys("Strings.resx");
        var french = Keys("Strings.fr.resx");

        neutral.Should().NotBeEmpty("the neutral resources must have been found at all");

        french.Except(neutral).Should().BeEmpty("these French keys no longer exist in the neutral file");
        neutral.Except(french).Should().BeEmpty("these strings have no French translation");
    }

    // A resx entry with no value is worse than a missing one: the fallback never runs, so the UI renders
    // an empty label rather than the English word.
    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.fr.resx")]
    public void No_string_is_left_blank(string file)
    {
        var blank =
            from entry in Entries(file)
            where string.IsNullOrWhiteSpace(entry.Element("value")?.Value)
            select entry.Attribute("name")!.Value;

        blank.Should().BeEmpty();
    }

    // `Text.Format` fills the placeholders, so a translation that dropped one renders a sentence missing
    // its version number — and one that invented an extra throws at the call site.
    [Fact]
    public void French_keeps_the_same_placeholders()
    {
        var neutral = Values("Strings.resx");

        foreach (var (key, french) in Values("Strings.fr.resx"))
        {
            if (!neutral.TryGetValue(key, out var original))
                continue;

            Placeholders(french).Should().Equal(
                Placeholders(original),
                $"{key} must carry the same placeholders as the neutral string");
        }
    }

    private static IReadOnlyList<int> Placeholders(string value)
        => Enumerable.Range(0, 10).Where(index => value.Contains($"{{{index}}}", StringComparison.Ordinal)).ToList();

    private static IReadOnlyDictionary<string, string> Values(string file)
        => Entries(file).ToDictionary(
            entry => entry.Attribute("name")!.Value,
            entry => entry.Element("value")?.Value ?? string.Empty);

    private static HashSet<string> Keys(string file)
        => [.. Entries(file).Select(entry => entry.Attribute("name")!.Value)];

    private static IEnumerable<XElement> Entries(string file)
        => XDocument.Load(Path.Combine(ResourceRoot(), file))
            .Root!
            .Elements("data")
            .Where(entry => entry.Attribute("name") is not null && entry.Attribute("mimetype") is null);

    private static string ResourceRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..", "src", "Act.App", "Resources"));
}
