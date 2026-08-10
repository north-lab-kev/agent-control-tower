using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Act.App.Resources;
using AwesomeAssertions;

namespace Act.App.UiTests;

// Wordings come in `_One`/`_Many` pairs by convention rather than by a type, which is only safe if
// something checks the convention holds. This is that something: a pair added in English and
// forgotten in French, a `_One` with no `_Many`, or a translation that dropped the `{0}` the count
// goes into fails here rather than reaching a user as a half-built sentence.
public class TextPluralTests
{
    private static readonly CultureInfo[] Shipped = [new("en"), new("fr")];

    // English does not inflect this one — the noun is in the tooltip, not the badge — so the pair is
    // deliberately identical there. Listed rather than skipped, so a pair that becomes identical by
    // accident still fails.
    private static readonly string[] InvariantInEnglish = ["Board_HiddenAttention"];

    [Theory]
    [InlineData(0, "many")]
    [InlineData(1, "one")]
    [InlineData(2, "many")]
    [InlineData(97, "many")]
    public void The_singular_is_for_exactly_one(int count, string expected)
        => Text.Plural(count, "one", "many").Should().Be(expected);

    // Not reachable through any caller — every one of them refuses zero first — but a negative count
    // reaching the plural rather than the singular is the answer that degrades into a readable
    // sentence rather than a wrong one.
    [Fact]
    public void A_count_below_zero_is_not_singular()
        => Text.Plural(-1, "one", "many").Should().Be("many");

    [Fact]
    public void Every_singular_has_a_plural()
    {
        var orphans = Keys()
            .Where(key => key.EndsWith("_One", StringComparison.Ordinal))
            .Where(key => !Keys().Contains(Sibling(key)))
            .ToList();

        orphans.Should().BeEmpty();
    }

    [Fact]
    public void Every_plural_has_a_singular()
    {
        var orphans = Keys()
            .Where(key => key.EndsWith("_Many", StringComparison.Ordinal))
            .Where(key => !Keys().Contains(Sibling(key)))
            .ToList();

        orphans.Should().BeEmpty();
    }

    [Fact]
    public void Both_wordings_exist_in_every_shipped_language()
    {
        foreach (var key in Pairs().SelectMany(pair => new[] { $"{pair}_One", $"{pair}_Many" }))
        {
            foreach (var culture in Shipped)
            {
                Strings.ResourceManager.GetString(key, culture)
                    .Should().NotBeNullOrWhiteSpace($"{key} needs wording in {culture.Name}");
            }
        }
    }

    // Where a language inflects, an identical pair is a copy-paste rather than a coincidence — and it
    // is wrong for exactly one count, which is the count these pairs exist for.
    [Fact]
    public void The_two_wordings_differ_wherever_the_language_inflects()
    {
        foreach (var pair in Pairs())
        {
            foreach (var culture in Shipped)
            {
                if (culture.Name is "en" && InvariantInEnglish.Contains(pair))
                    continue;

                Wording($"{pair}_One", culture)
                    .Should().NotBe(Wording($"{pair}_Many", culture), $"{pair} inflects in {culture.Name}");
            }
        }
    }

    // A translation that lost the `{0}` renders a sentence with no number in it, and a translation
    // that invented a `{1}` throws at the format call rather than at build time.
    [Fact]
    public void A_translation_carries_the_same_placeholders_as_the_english()
    {
        foreach (var key in Pairs().SelectMany(pair => new[] { $"{pair}_One", $"{pair}_Many" }))
        {
            var english = Placeholders(Wording(key, Shipped[0]));

            foreach (var culture in Shipped.Skip(1))
                Placeholders(Wording(key, culture)).Should().Equal(english, $"{key} in {culture.Name}");
        }
    }

    // The caller formats both halves against one argument list, sized for the plural because that is
    // the half that always names the count. A singular reaching past it throws for exactly the count
    // nobody thinks to try.
    [Fact]
    public void A_singular_never_asks_for_more_than_its_plural()
    {
        foreach (var pair in Pairs())
        {
            foreach (var culture in Shipped)
            {
                Placeholders(Wording($"{pair}_One", culture))
                    .Should().BeSubsetOf(
                        Placeholders(Wording($"{pair}_Many", culture)),
                        $"{pair} in {culture.Name}");
            }
        }
    }

    private static string Wording(string key, CultureInfo culture)
        => Strings.ResourceManager.GetString(key, culture)!;

    private static int[] Placeholders(string wording)
        => Regex.Matches(wording, @"\{(\d+)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();

    private static string Sibling(string key)
        => key.EndsWith("_One", StringComparison.Ordinal)
            ? string.Concat(key.AsSpan(0, key.Length - 4), "_Many")
            : string.Concat(key.AsSpan(0, key.Length - 5), "_One");

    private static IEnumerable<string> Pairs()
        => Keys()
            .Where(key => key.EndsWith("_One", StringComparison.Ordinal))
            .Select(key => key[..^4]);

    // Read off the neutral resx rather than a hand-kept list, so a pair added tomorrow is swept
    // without anybody remembering to add it here.
    private static HashSet<string> Keys([CallerFilePath] string here = "")
    {
        var resx = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..", "src", "Act.App", "Resources", "Strings.resx"));

        return
        [
            .. XDocument.Load(resx).Root!.Elements("data")
                .Select(data => data.Attribute("name")?.Value)
                .Where(name => name is not null)
                .Select(name => name!),
        ];
    }
}
