using Act.Core.Model;
using Act.Core.Spawning;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class SpawnQuotaTests
{
    [Fact]
    public void A_card_that_has_spawned_nothing_has_its_whole_budget()
        => SpawnQuota.Exhausted(Parent(0), 100).Should().BeFalse();

    [Fact]
    public void The_last_slot_is_still_a_slot()
        => SpawnQuota.Exhausted(Parent(99), 100).Should().BeFalse();

    [Fact]
    public void The_cap_is_reached_at_the_cap_not_past_it()
        => SpawnQuota.Exhausted(Parent(100), 100).Should().BeTrue();

    // Counted off `children[]`, which is stored — so a relaunch, a restore or an app restart cannot
    // hand a looping agent a fresh hundred.
    [Fact]
    public void The_budget_is_the_cards_for_its_lifetime_not_its_sessions()
        => SpawnQuota.Exhausted(Parent(140), 100).Should().BeTrue();

    [Fact]
    public void Zero_is_a_legitimate_answer_and_means_no_agent_spawned_work()
        => SpawnQuota.Exhausted(Parent(0), 0).Should().BeTrue();

    [Fact]
    public void A_cap_outside_the_bounds_is_clamped_rather_than_honoured()
    {
        SpawnQuota.Clamp(-5).Should().Be(SpawnQuota.Minimum);
        SpawnQuota.Clamp(50_000).Should().Be(SpawnQuota.Maximum);
        SpawnQuota.Exhausted(Parent(SpawnQuota.Maximum), int.MaxValue).Should().BeTrue();
    }

    private static Card Parent(int children)
    {
        var card = new Card();

        for (var index = 0; index < children; index++)
            card.Children.Add(Guid.NewGuid());

        return card;
    }
}
