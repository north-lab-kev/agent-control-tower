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

    [Fact]
    public void Progress_refines_the_download_it_belongs_to()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 0));

        state.Changed += () => changes++;

        state.PublishProgress("1.2.0", 40);

        state.Current.Should().Be(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 40));
        changes.Should().Be(1);
    }

    // The whole point. A report arriving after the download resolved must not walk `Ready` back to
    // `Downloading` — nothing revisits that state, so the page would keep the stale percentage and
    // never say the update is waiting to install.
    [Fact]
    public void Progress_that_arrives_after_the_download_is_refused()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 60));
        state.Publish(new UpdateStatus(UpdateStage.Ready, "1.2.0", 100));

        state.Changed += () => changes++;

        state.PublishProgress("1.2.0", 60);

        state.Current.Stage.Should().Be(UpdateStage.Ready);
        state.Current.Percent.Should().Be(100);
        changes.Should().Be(0, "a refused write is not news either");
    }

    [Fact]
    public void Progress_that_arrives_after_a_failed_download_is_refused()
    {
        var state = new UpdateState();

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 60));
        state.Publish(new UpdateStatus(UpdateStage.Available, "1.2.0"));

        state.PublishProgress("1.2.0", 60);

        state.Current.Stage.Should().Be(UpdateStage.Available);
    }

    // A download that was superseded still holds a `Progress<int>` nobody unsubscribed, and its
    // percentage means nothing about the version now on screen.
    [Fact]
    public void Progress_for_another_version_is_refused()
    {
        var state = new UpdateState();

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.3.0", 10));

        state.PublishProgress("1.2.0", 90);

        state.Current.Should().Be(new UpdateStatus(UpdateStage.Downloading, "1.3.0", 10));
    }

    [Fact]
    public void Progress_before_anything_is_downloading_is_refused()
    {
        var state = new UpdateState();

        state.PublishProgress("1.2.0", 40);

        state.Current.Should().Be(UpdateStatus.Idle);
    }

    [Fact]
    public void Progress_that_has_not_moved_says_nothing()
    {
        var state = new UpdateState();
        var changes = 0;

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 40));

        state.Changed += () => changes++;

        state.PublishProgress("1.2.0", 40);

        changes.Should().Be(0);
    }

    // `CheckedAt` belongs to the check that found the version, not to the report — a refinement that
    // dropped it would blank the "last checked" line on the settings page mid-download.
    [Fact]
    public void Progress_keeps_the_rest_of_the_status()
    {
        var state = new UpdateState();
        var at = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        state.Publish(new UpdateStatus(UpdateStage.Downloading, "1.2.0", 0, at));

        state.PublishProgress("1.2.0", 40);

        state.Current.CheckedAt.Should().Be(at);
        state.Current.Version.Should().Be("1.2.0");
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
