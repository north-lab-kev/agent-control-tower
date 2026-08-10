using System.Globalization;
using Act.App.Desktop;
using Act.App.Resources;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

public class TrayTooltipTests
{
    [Fact]
    public void An_empty_board_just_names_the_app()
        => TrayTooltip.For([]).Should().Be(Strings.Shell_FullName);

    [Fact]
    public void So_does_a_board_with_nothing_waiting()
        => TrayTooltip.For([Card(BoardColumn.Executing, Badge.Running)])
            .Should().Be(Strings.Shell_FullName);

    [Fact]
    public void One_waiting_card_takes_the_singular()
        => TrayTooltip.For([Card(BoardColumn.YourTurn, Badge.NeedsAnswer)])
            .Should().Be(Expected(Strings.Tray_Tooltip_One, 1));

    // Two is where the singular stops, and off-by-one there is the whole bug this pair of wordings
    // exists to avoid.
    [Fact]
    public void Two_is_already_plural()
        => TrayTooltip.For(
        [
            Card(BoardColumn.YourTurn, Badge.NeedsAnswer),
            Card(BoardColumn.YourTurn, Badge.Killed),
        ])
            .Should().Be(Expected(Strings.Tray_Tooltip_Many, 2));

    [Fact]
    public void Every_waiting_card_is_counted()
        => TrayTooltip.For(
        [
            Card(BoardColumn.YourTurn, Badge.NeedsAnswer),
            Card(BoardColumn.YourTurn, Badge.Error),
            Card(BoardColumn.YourTurn, Badge.ReadyForReview),
        ])
            .Should().Be(Expected(Strings.Tray_Tooltip_Many, 3));

    // The tooltip is the same claim as the toast and the blink, so it counts with the same rule —
    // a card in Your turn on a work-in-flight badge is not waiting for anybody.
    [Theory]
    [InlineData(Badge.Running)]
    [InlineData(Badge.Compacting)]
    [InlineData(null)]
    public void Work_in_flight_in_your_turn_is_not_waiting(Badge? badge)
        => TrayTooltip.For([Card(BoardColumn.YourTurn, badge)]).Should().Be(Strings.Shell_FullName);

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.Completed)]
    public void No_other_column_is_waiting(BoardColumn column)
        => TrayTooltip.For([Card(column, Badge.NeedsPermission)]).Should().Be(Strings.Shell_FullName);

    [Fact]
    public void A_card_off_the_board_is_not_waiting()
    {
        var archived = Card(BoardColumn.YourTurn, Badge.Error);
        archived.ArchivedAt = DateTimeOffset.UnixEpoch;

        var deleted = Card(BoardColumn.YourTurn, Badge.Error);
        deleted.DeletedAt = DateTimeOffset.UnixEpoch;

        TrayTooltip.For([archived, deleted]).Should().Be(Strings.Shell_FullName);
    }

    [Fact]
    public void Only_the_waiting_ones_are_counted()
        => TrayTooltip.For(
        [
            Card(BoardColumn.YourTurn, Badge.NeedsPermission),
            Card(BoardColumn.Executing, Badge.Running),
            Card(BoardColumn.Ready, null),
        ])
            .Should().Be(Expected(Strings.Tray_Tooltip_One, 1));

    // Read through the resource manager rather than by switching the ambient culture, which the
    // rest of the suite is running in parallel with.
    [Theory]
    [InlineData("Tray_Tooltip_One")]
    [InlineData("Tray_Tooltip_Many")]
    public void Each_wording_is_translated_in_every_shipped_language(string key)
    {
        var english = Strings.ResourceManager.GetString(key, new CultureInfo("en"));
        var french = Strings.ResourceManager.GetString(key, new CultureInfo("fr"));

        english.Should().NotBeNullOrWhiteSpace();
        french.Should().NotBeNullOrWhiteSpace().And.NotBe(english);
    }

    // A plural copy-pasted over the singular reads perfectly and is wrong for exactly one count,
    // which is the count a tray icon spends most of its time showing.
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void The_two_wordings_differ_within_a_language(string language)
    {
        var culture = new CultureInfo(language);

        Strings.ResourceManager.GetString("Tray_Tooltip_One", culture)
            .Should().NotBe(Strings.ResourceManager.GetString("Tray_Tooltip_Many", culture));
    }

    // Read literally in both languages, because everything above compares one resource against
    // another and would go on passing if the noun never changed.
    [Theory]
    [InlineData("en", "{1} task waiting", "{1} tasks waiting")]
    [InlineData("fr", "{1} tâche en attente", "{1} tâches en attente")]
    public void The_noun_is_singular_then_plural(string language, string one, string many)
    {
        var culture = new CultureInfo(language);

        Strings.ResourceManager.GetString("Tray_Tooltip_One", culture).Should().Contain(one);
        Strings.ResourceManager.GetString("Tray_Tooltip_Many", culture).Should().Contain(many);
    }

    [Theory]
    [InlineData("Tray_Tooltip_One")]
    [InlineData("Tray_Tooltip_Many")]
    public void Every_wording_takes_the_name_and_the_count(string key)
    {
        foreach (var culture in new[] { new CultureInfo("en"), new CultureInfo("fr") })
        {
            var wording = Strings.ResourceManager.GetString(key, culture)!;

            string.Format(culture, wording, Strings.Shell_FullName, 2)
                .Should().Contain(Strings.Shell_FullName).And.Contain("2");
        }
    }

    // The wording each count is supposed to reach for, so the assertions above pin the *choice* of
    // wording without also pinning the language the suite happens to run in.
    private static string Expected(string wording, int waiting)
        => Text.Format(wording, Strings.Shell_FullName, waiting);

    private static Card Card(BoardColumn column, Badge? badge)
        => new() { Column = column, Badge = badge };
}
