using Act.App.Updates;
using AwesomeAssertions;

namespace Act.App.UiTests;

public class UpdateStateTests
{
    [Fact]
    public void It_starts_idle()
        => new UpdateState().Current.Should().Be(UpdateStatus.Idle);

    [Fact]
    public void Publishing_replaces_the_status_and_says_so()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Changed += () => changes++;

        state.Publish(new UpdateStatus(UpdateStage.Available, "1.2.0"));

        state.Current.Version.Should().Be("1.2.0");
        changes.Should().Be(1);
    }

    // A download reports progress several times a second, and most of those reports carry the
    // percent that was already on screen. Re-rendering the settings page for each of them is the
    // version of this feature that shows up as a busy CPU.
    [Fact]
    public void Publishing_the_same_status_again_says_nothing()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 40));

        state.Changed += () => changes++;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 40));

        changes.Should().Be(0);
    }

    [Fact]
    public void A_moved_percentage_is_news()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 40));

        state.Changed += () => changes++;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 41));

        changes.Should().Be(1);
    }

    [Theory]
    [InlineData(UpdateStage.Checking, true)]
    [InlineData(UpdateStage.Downloading, true)]
    [InlineData(UpdateStage.Idle, false)]
    [InlineData(UpdateStage.UpToDate, false)]
    [InlineData(UpdateStage.Available, false)]
    [InlineData(UpdateStage.Ready, false)]
    [InlineData(UpdateStage.Unavailable, false)]
    public void Busy_is_the_two_stages_that_are_waiting_on_something(UpdateStage stage, bool busy)
        => new UpdateStatus(stage).IsBusy.Should().Be(busy);
}
