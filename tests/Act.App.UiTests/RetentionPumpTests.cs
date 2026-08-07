using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The hourly auto-archive sweep. Which cards are due is `CompletedRetention`'s answer and is tested
// in Core; what is tested here is the pump — that a sweep happens without waiting an hour for the
// first one, that changing the window runs one immediately, and that a disposed pump is quiet.
public class RetentionPumpTests : ComponentTest
{
    [Fact]
    public async Task Starting_sweeps_without_waiting_for_the_first_hour()
    {
        var old = await ACompletedCard(Now.AddDays(-30));

        Retention(10);

        await using var pump = Pump();

        pump.Start();

        await Until(() => old.ArchivedAt is not null);

        Board.In(BoardColumn.Completed).Should().BeEmpty();
    }

    [Fact]
    public async Task A_card_inside_the_window_is_left_alone()
    {
        var recent = await ACompletedCard(Now.AddDays(-2));

        Retention(10);

        await using var pump = Pump();

        pump.Start();

        await Task.Delay(100);

        recent.ArchivedAt.Should().BeNull();
        Board.In(BoardColumn.Completed).Should().ContainSingle();
    }

    // A null window is what the switch being off looks like by the time the sweep reads it, and it
    // has to mean "keep everything" rather than "keep nothing".
    [Fact]
    public async Task Auto_archive_switched_off_sweeps_nothing()
    {
        var old = await ACompletedCard(Now.AddDays(-30));

        Settings.SetAutoArchiveCompleted(false);

        await using var pump = Pump();

        pump.Start();

        await Task.Delay(100);

        old.ArchivedAt.Should().BeNull();
    }

    // Through the same gate as the hourly sweep, so shortening the window takes effect at once
    // rather than at the top of the next hour.
    [Fact]
    public async Task Changing_the_window_runs_a_sweep_rather_than_waiting_for_the_next_hour()
    {
        var old = await ACompletedCard(Now.AddDays(-30));

        Retention(90);

        await using var pump = Pump();

        pump.Start();

        await Task.Delay(100);

        old.ArchivedAt.Should().BeNull("30 days is inside a 90-day window");

        Settings.SetAutoArchiveCompletedAfterDays(10);

        await Until(() => old.ArchivedAt is not null);
    }

    [Fact]
    public async Task A_disposed_pump_stops_answering_settings_changes()
    {
        var old = await ACompletedCard(Now.AddDays(-30));

        Settings.SetAutoArchiveCompleted(false);

        var pump = Pump();

        pump.Start();

        await Task.Delay(100);

        await pump.DisposeAsync();

        Retention(10);

        await Task.Delay(100);

        old.ArchivedAt.Should().BeNull("the pump is no longer listening");
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        var pump = Pump();

        pump.Start();

        await pump.DisposeAsync();

        var again = async () => await pump.DisposeAsync();

        await again.Should().NotThrowAsync();
    }

    private RetentionPump Pump() => new(Board, Settings, NullLogger<RetentionPump>.Instance);

    private void Retention(int days)
    {
        Settings.SetAutoArchiveCompletedAfterDays(days);
        Settings.SetAutoArchiveCompleted(true);
    }

    private async Task<Card> ACompletedCard(DateTimeOffset completedAt)
    {
        var card = new Card
        {
            Number = 1,
            Title = "Signed off",
            Column = BoardColumn.Completed,
            CompletedAt = completedAt,
        };

        await BoardWith(card);

        return card;
    }

    private static async Task Until(Func<bool> settled)
    {
        for (var waited = 0; waited < 200; waited++)
        {
            if (settled())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The sweep never reached the expected state.");
    }
}
