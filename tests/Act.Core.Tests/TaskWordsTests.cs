using Act.Core.Model;
using Act.Core.Spawning;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The law the single-table shape exists to keep: every word ACT gives an agent is a word the parse
// accepts back, so `get_task` can never answer with a value `create_followup` then refuses. Theories
// over `Enum.GetValues` rather than hand-listed members, so a new member fails here until its row is
// added.
public class TaskWordsTests
{
    [Theory]
    [MemberData(nameof(Columns))]
    public void Every_column_word_round_trips(BoardColumn column)
    {
        TaskWords.TryParse(TaskWords.Of(column), out BoardColumn parsed).Should().BeTrue();

        parsed.Should().Be(column);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void Every_agent_word_round_trips(AgentType agent)
    {
        TaskWords.TryParse(TaskWords.Of(agent), out AgentType parsed).Should().BeTrue();

        parsed.Should().Be(agent);
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Every_permission_word_round_trips(PermissionMode mode)
    {
        TaskWords.TryParse(TaskWords.Of(mode), out PermissionMode parsed).Should().BeTrue();

        parsed.Should().Be(mode);
    }

    // `scheduled` is the one deliberate exception: an agent may be told a card is scheduled, but
    // cannot ask for it — there is no datetime argument for it to pair with.
    [Theory]
    [MemberData(nameof(Schedules))]
    public void Every_schedule_word_round_trips_except_the_datetime_one(TaskSchedule schedule)
    {
        var accepted = TaskWords.TryParse(TaskWords.Of(schedule), out TaskSchedule parsed);

        if (schedule == TaskSchedule.SpecificDateTime)
        {
            accepted.Should().BeFalse();

            return;
        }

        accepted.Should().BeTrue();
        parsed.Should().Be(schedule);
    }

    [Theory]
    [InlineData("your turn")]
    [InlineData("YOUR_TURN")]
    [InlineData(" ready ")]
    public void The_column_parse_is_forgiving_about_spelling(string asked)
        => TaskWords.TryParse(asked, out BoardColumn _).Should().BeTrue();

    [Theory]
    [InlineData("done")]
    [InlineData("in_progress")]
    [InlineData("")]
    [InlineData(null)]
    public void A_word_outside_the_vocabulary_is_not_parsed(string? asked)
        => TaskWords.TryParse(asked, out BoardColumn _).Should().BeFalse();

    // What a refusal shows is derived from the same rows the parse reads, so the list in the message
    // can never offer a word the parse would then refuse.
    [Fact]
    public void The_offered_lists_are_the_accepted_vocabulary()
    {
        TaskWords.ColumnWords.Should().Be("preparing, ready, executing, your_turn, completed");
        TaskWords.AgentWords.Should().Be("claude, codex");
        TaskWords.PermissionWords.Should().Be("default, plan, acceptEdits, auto, dontAsk, bypass");
        TaskWords.ScheduleWords.Should().Be("manual, now, next_window");
    }

    public static TheoryData<BoardColumn> Columns => [.. Enum.GetValues<BoardColumn>()];

    public static TheoryData<AgentType> Agents => [.. Enum.GetValues<AgentType>()];

    public static TheoryData<PermissionMode> Permissions => [.. Enum.GetValues<PermissionMode>()];

    public static TheoryData<TaskSchedule> Schedules => [.. Enum.GetValues<TaskSchedule>()];
}
