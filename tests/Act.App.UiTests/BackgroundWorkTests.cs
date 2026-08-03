using Act.App.Hosting;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The lifetime the five pumps used to each write out by hand. These are the two things the copies
// had drifted apart on — whether shutdown waits for the work it started, and whether one bad pass
// ends the loop — plus the coalescing the queue depends on.
public class BackgroundWorkTests
{
    [Fact]
    public async Task Disposal_waits_for_the_work_it_started()
    {
        var finished = false;

        await using (var work = Work())
        {
            work.Start("slow work", async token =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    // The teardown a real pump does on its way out, which a disposal that did not
                    // wait would race.
                    await Task.Delay(20, CancellationToken.None);

                    finished = true;

                    throw;
                }
            });

            await Settles(() => true);
        }

        finished.Should().BeTrue();
    }

    // The failure that used to be fatal. A `catch` around the loop means one bad pass ends the
    // polling for the life of the process; the catch belongs around the pass.
    [Fact]
    public async Task A_failing_pass_does_not_end_the_loop()
    {
        var passes = 0;

        await using var work = Work();

        work.StartLoop("a loop", TimeSpan.FromMilliseconds(10), _ =>
        {
            passes++;

            throw new InvalidOperationException("this pass is broken");
        });

        await Settles(() => passes >= 3);

        passes.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_loop_runs_its_first_pass_before_the_first_interval()
    {
        var passes = 0;

        await using var work = Work();

        work.StartLoop("a loop", TimeSpan.FromHours(1), _ =>
        {
            passes++;

            return Task.CompletedTask;
        });

        await Settles(() => passes > 0);

        passes.Should().Be(1);
    }

    // What stops a pass that writes to the board from scheduling a pass per write.
    [Fact]
    public async Task Requests_arriving_during_a_pass_collapse_into_one()
    {
        var started = 0;
        var release = new TaskCompletionSource();

        await using var work = Work();

        Task Pass(CancellationToken token)
        {
            started++;

            return started == 1 ? release.Task : Task.CompletedTask;
        }

        work.Request("a pass", Pass);

        await Settles(() => started == 1);

        // Five more while the first is still in flight: one follow-up, not five.
        for (var i = 0; i < 5; i++)
            work.Request("a pass", Pass);

        release.SetResult();

        await Settles(() => started == 2);
        await Task.Delay(50);

        started.Should().Be(2);
    }

    [Fact]
    public async Task Work_started_after_disposal_is_ignored()
    {
        var ran = false;

        var work = Work();

        await work.DisposeAsync();

        work.Start("late work", _ =>
        {
            ran = true;

            return Task.CompletedTask;
        });

        ran.Should().BeFalse();
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        var work = Work();

        work.StartLoop("a loop", TimeSpan.FromMilliseconds(10), _ => Task.CompletedTask);

        await work.DisposeAsync();
        await work.DisposeAsync();
    }

    private static BackgroundWork Work() => new(NullLogger.Instance);

    private static async Task Settles(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
            await Task.Delay(10);
    }
}
