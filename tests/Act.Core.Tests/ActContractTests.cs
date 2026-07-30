using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class ActContractTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private static readonly Guid OtherTaskId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Both_directories_sit_under_the_act_root()
    {
        var root = ActContract.RootDirectory("C:/repo");

        ActContract.StatusDirectory("C:/repo").Should().StartWith(root);
        ActContract.FollowUpsDirectory("C:/repo").Should().StartWith(root);
    }

    [Fact]
    public void Consumed_follow_ups_stay_inside_the_follow_up_directory()
        => ActContract.ConsumedDirectory("C:/repo").Should()
            .StartWith(ActContract.FollowUpsDirectory("C:/repo"));

    [Fact]
    public void The_file_prefix_is_the_task_id()
        => ActContract.FilePrefix(TaskId).Should().Be($"{TaskId:d}-");

    [Fact]
    public void A_status_file_is_named_for_its_task_and_turn()
        => ActContract.StatusFileName(TaskId, 3).Should().Be($"{TaskId:d}-3.json");

    [Fact]
    public void A_file_belongs_to_the_task_whose_prefix_it_carries()
    {
        ActContract.BelongsToTask(ActContract.StatusFileName(TaskId, 1), TaskId).Should().BeTrue();
        ActContract.BelongsToTask(ActContract.StatusFileName(OtherTaskId, 1), TaskId).Should().BeFalse();
    }

    [Fact]
    public void A_full_path_is_matched_by_its_file_name_alone()
        => ActContract.BelongsToTask(
            Path.Combine(ActContract.StatusDirectory("C:/repo"), ActContract.StatusFileName(TaskId, 1)),
            TaskId).Should().BeTrue();
}
