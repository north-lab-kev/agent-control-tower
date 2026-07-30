using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CardTests
{
    [Theory]
    [InlineData(Badge.NeedsPermission)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.Error)]
    public void A_blocked_card_needs_attention(Badge badge)
        => new Card { Badge = badge }.NeedsAttention.Should().BeTrue();

    [Theory]
    [InlineData(Badge.Running)]
    [InlineData(Badge.Compacting)]
    [InlineData(Badge.Stale)]
    [InlineData(Badge.Idle)]
    public void A_progressing_card_does_not_need_attention(Badge badge)
        => new Card { Badge = badge }.NeedsAttention.Should().BeFalse();

    [Fact]
    public void A_card_without_a_badge_does_not_need_attention()
        => new Card().NeedsAttention.Should().BeFalse();

    [Fact]
    public void A_new_card_starts_in_preparing_with_its_own_id()
    {
        var card = new Card();

        card.Id.Should().NotBe(Guid.Empty);
        card.Column.Should().Be(BoardColumn.Preparing);
        card.Origin.Should().Be(TaskOrigin.Manual);
        card.Badge.Should().BeNull();
        card.Schedule.Should().BeNull();
        card.Children.Should().BeEmpty();
        card.Transitions.Should().BeEmpty();
    }

    [Fact]
    public void Total_tokens_add_both_directions()
        => new CardMetrics { TokensIn = 312_400, TokensOut = 44_100 }
            .TokensTotal.Should().Be(356_500);

    [Fact]
    public void Context_percent_is_derived_from_the_window()
        => new CardMetrics { ContextUsed = 142_000, ContextLimit = 200_000 }
            .ContextPercent.Should().Be(71);

    [Fact]
    public void Context_percent_is_unknown_without_a_window()
        => new CardMetrics { ContextUsed = 142_000 }.ContextPercent.Should().BeNull();
}
