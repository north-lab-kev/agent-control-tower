using System.Globalization;
using Act.App.Notifications;
using Act.App.Settings;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

public class NotificationDispatcherTests
{
    private static readonly Guid Board = Guid.NewGuid();

    private static readonly CultureInfo English = new("en");

    [Fact]
    public void A_card_that_wants_you_is_announced()
    {
        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(Blocked());

        notifier.Shown.Should().ContainSingle()
            .Which.Body.Should().Be("#1042 · Rename the widget");
    }

    [Fact]
    public void The_title_says_which_kind_it_is()
    {
        CultureInfo.CurrentUICulture = English;

        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(Blocked());

        notifier.Shown.Single().Title.Should().Be("Needs permission");
    }

    [Fact]
    public void The_notification_names_the_card_so_a_click_can_reach_it()
    {
        var card = Blocked();
        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(card);

        notifier.Shown.Single().TaskId.Should().Be(card.Id);
    }

    [Fact]
    public void Work_still_running_is_not_announced()
    {
        var card = Blocked();
        card.Column = BoardColumn.Executing;
        card.Badge = Badge.Running;

        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(card);

        notifier.Shown.Should().BeEmpty();
    }

    // The same state must never ping twice: several sources report the same block, and a card can be
    // written again for reasons that are not a change at all.
    [Fact]
    public void The_same_state_is_announced_once()
    {
        var card = Blocked();
        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(card);
        dispatcher.Notify(card);
        dispatcher.Notify(card);

        notifier.Shown.Should().ContainSingle();
    }

    [Fact]
    public void A_different_reason_on_the_same_card_is_new_news()
    {
        CultureInfo.CurrentUICulture = English;

        var card = Blocked();
        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(card);

        card.Badge = Badge.ReadyForReview;
        dispatcher.Notify(card);

        notifier.Shown.Select(shown => shown.Title)
            .Should().Equal("Needs permission", "Ready for review");
    }

    // The user answered in the terminal, the card went back to work, and then it blocks again — that
    // second block is a fresh reason to be told.
    [Fact]
    public void Blocking_again_after_the_card_went_back_to_work_pings_again()
    {
        var card = Blocked();
        var (dispatcher, notifier, _) = DispatcherOf();

        dispatcher.Notify(card);

        card.Column = BoardColumn.Executing;
        card.Badge = Badge.Running;
        dispatcher.Notify(card);

        card.Column = BoardColumn.YourTurn;
        card.Badge = Badge.NeedsPermission;
        dispatcher.Notify(card);

        notifier.Shown.Should().HaveCount(2);
    }

    [Fact]
    public void Nothing_is_announced_while_the_setting_is_off()
    {
        var (dispatcher, notifier, settings) = DispatcherOf();

        settings.SetNotifications(false);

        dispatcher.Notify(Blocked());

        notifier.Shown.Should().BeEmpty();
    }

    // The in-app pulse is already saying it, on the surface the user is looking at.
    [Fact]
    public void Nothing_is_announced_while_the_user_is_watching_the_board()
    {
        var presence = new UiPresence();
        var (dispatcher, notifier, _) = DispatcherOf(presence);

        presence.Report(Board, focused: true, route: string.Empty);

        dispatcher.Notify(Blocked());

        notifier.Shown.Should().BeEmpty();
    }

    [Fact]
    public void A_window_left_on_another_page_is_still_told()
    {
        var presence = new UiPresence();
        var (dispatcher, notifier, _) = DispatcherOf(presence);

        presence.Report(Board, focused: true, route: "card/9f0d/terminal");

        dispatcher.Notify(Blocked());

        notifier.Shown.Should().ContainSingle();
    }

    [Fact]
    public void A_board_nobody_is_looking_at_is_told()
    {
        var presence = new UiPresence();
        var (dispatcher, notifier, _) = DispatcherOf(presence);

        presence.Report(Board, focused: false, route: string.Empty);

        dispatcher.Notify(Blocked());

        notifier.Shown.Should().ContainSingle();
    }

    // A state suppressed because the user was watching must not fire later when they walk away: the
    // notification is about the moment it changed, and that moment was seen.
    [Fact]
    public void A_state_seen_on_the_board_is_not_replayed_when_the_window_loses_focus()
    {
        var card = Blocked();
        var presence = new UiPresence();
        var (dispatcher, notifier, _) = DispatcherOf(presence);

        presence.Report(Board, focused: true, route: string.Empty);
        dispatcher.Notify(card);

        presence.Report(Board, focused: false, route: string.Empty);
        dispatcher.Notify(card);

        notifier.Shown.Should().BeEmpty();
    }

    private static (NotificationDispatcher, RecordingNotifier, UserSettingsService) DispatcherOf(
        UiPresence? presence = null)
    {
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());

        var notifier = new RecordingNotifier();

        return (TestNotifications.Dispatcher(settings, notifier, presence), notifier, settings);
    }

    private static Card Blocked() => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        Column = BoardColumn.YourTurn,
        Badge = Badge.NeedsPermission,
    };
}
