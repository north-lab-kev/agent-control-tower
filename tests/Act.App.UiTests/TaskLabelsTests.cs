using System.Globalization;
using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The vocabulary every form that describes a task shares. It exists so the same value reads the same
// way wherever it is offered, which makes "every member of the enum resolves to something" the whole
// contract — a mode that fell through to its own `ToString` is the one that ships untranslated.
public class TaskLabelsTests
{
    public TaskLabelsTests()
        => CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");

    [Theory]
    [MemberData(nameof(Agents))]
    public void Every_agent_has_a_name_of_its_own(AgentType agent)
        => TaskLabels.Agent(agent).Should().NotBeNullOrWhiteSpace().And.NotBe(agent.ToString());

    [Theory]
    [MemberData(nameof(Modes))]
    public void Every_permission_mode_has_a_label_and_a_hint(PermissionMode mode)
    {
        TaskLabels.Permission(mode).Should().NotBeNullOrWhiteSpace()
            .And.NotBe(mode.ToString(), "the enum name is the fallback for a mode nobody translated");

        TaskLabels.PermissionHint(mode).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void No_two_permission_modes_read_the_same()
        => Enum.GetValues<PermissionMode>().Select(TaskLabels.Permission)
            .Should().OnlyHaveUniqueItems();

    [Theory]
    [MemberData(nameof(Schedules))]
    public void Every_schedule_has_a_label(TaskSchedule schedule)
        => TaskLabels.Schedule(schedule).Should().NotBeNullOrWhiteSpace()
            .And.NotBe(schedule.ToString());

    [Theory]
    [MemberData(nameof(GitActions))]
    public void Every_git_action_has_a_label(GitAction action)
        => TaskLabels.Git(action).Should().NotBeNullOrWhiteSpace().And.NotBe(action.ToString());

    [Fact]
    public void No_two_git_actions_read_the_same()
        => Enum.GetValues<GitAction>().Select(TaskLabels.Git).Should().OnlyHaveUniqueItems();

    // A specific datetime describes one task, never a kind of task, so the forms that pre-fill a task
    // rather than being one do not offer it.
    [Fact]
    public void A_specific_datetime_is_offered_only_where_a_real_task_is_being_described()
    {
        TaskLabels.Schedules(withDateTime: true).Select(choice => choice.Value)
            .Should().Equal(
                TaskSchedule.Manual,
                TaskSchedule.Now,
                TaskSchedule.NextWindow,
                TaskSchedule.SpecificDateTime);

        TaskLabels.Schedules(withDateTime: false).Select(choice => choice.Value)
            .Should().NotContain(TaskSchedule.SpecificDateTime);
    }

    [Fact]
    public void A_schedule_choice_carries_the_same_text_the_label_gives()
        => TaskLabels.Schedules(withDateTime: true)
            .Should().AllSatisfy(choice => choice.Text.Should().Be(TaskLabels.Schedule(choice.Value)));

    [Fact]
    public void The_git_choices_offer_every_action_in_escalating_order()
        => TaskLabels.GitActions().Select(choice => choice.Value)
            .Should().Equal(GitAction.Commit, GitAction.Push, GitAction.PullRequest);

    // Powers of 1024 with the short units, because the number is read beside a file name to answer
    // "is this the big one or the small one" — not to be added up.
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024 - 1, "1024 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(5 * 1024 * 1024 + 512 * 1024, "5.5 MB")]
    public void A_file_size_reads_in_the_unit_it_belongs_to(long bytes, string expected)
        => TaskLabels.FileSize(bytes).Should().Be(expected);

    // Culture-aware so a French decimal comma arrives as one.
    [Fact]
    public void A_file_size_follows_the_culture_the_app_is_running_in()
    {
        CultureInfo.CurrentCulture = new CultureInfo("fr");

        TaskLabels.FileSize(1536).Should().Be("1,5 KB");
    }

    // The default template's name is not stored: it is not the user's to change, and a stored
    // translation would freeze whichever language the install first ran in.
    [Fact]
    public void The_default_template_is_named_at_render_time_rather_than_from_the_store()
    {
        var stored = new TaskTemplate { IsDefault = true, Name = string.Empty };

        TaskLabels.Template(stored).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_other_template_is_named_by_the_user()
        => TaskLabels.Template(new TaskTemplate { Name = "Nightly sweep" })
            .Should().Be("Nightly sweep");

    public static TheoryData<AgentType> Agents => [.. Enum.GetValues<AgentType>()];

    public static TheoryData<PermissionMode> Modes => [.. Enum.GetValues<PermissionMode>()];

    public static TheoryData<TaskSchedule> Schedules => [.. Enum.GetValues<TaskSchedule>()];

    public static TheoryData<GitAction> GitActions => [.. Enum.GetValues<GitAction>()];
}
