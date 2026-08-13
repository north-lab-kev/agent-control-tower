using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class WorkingDirConflictTests
{
    [Fact]
    public void A_ready_card_is_blocked_by_a_card_executing_in_the_same_folder()
        => Blocking(Ready(), Held(BoardColumn.Executing)).Should().NotBeNull();

    // The guard reads "until completed", not "while running": a card in Your turn is parked at a
    // live prompt with a working tree in whatever state its agent left it, so the folder is no
    // freer than it was mid-turn.
    [Fact]
    public void A_card_waiting_on_the_user_still_holds_its_folder()
        => Blocking(Ready(), Held(BoardColumn.YourTurn)).Should().NotBeNull();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public void A_card_outside_the_machine_region_holds_nothing(BoardColumn column)
        => Blocking(Ready(), Held(column)).Should().BeNull();

    [Fact]
    public void An_archived_card_holds_nothing()
    {
        var holder = Held(BoardColumn.Executing);
        holder.ArchivedAt = DateTimeOffset.UtcNow;

        Blocking(Ready(), holder).Should().BeNull();
    }

    [Fact]
    public void A_card_working_in_another_folder_blocks_nothing()
    {
        var holder = Held(BoardColumn.Executing);
        holder.WorkingDir = "/dev/other";

        Blocking(Ready(), holder).Should().BeNull();
    }

    // The whole reason the comparison resolves first: a card stores what the user typed, and two
    // spellings of one folder must not read as two folders.
    [Theory]
    [InlineData("/dev/act/")]
    [InlineData(@"\dev\act")]
    [InlineData("  /dev/act  ")]
    public void The_same_folder_spelled_differently_is_the_same_folder(string typed)
    {
        var card = Ready();
        card.WorkingDir = typed;

        Blocking(card, Held(BoardColumn.Executing)).Should().NotBeNull();
    }

    [Fact]
    public void A_home_relative_path_matches_the_folder_it_expands_to()
    {
        var card = Ready();
        card.WorkingDir = "~/dev/act";

        var holder = Held(BoardColumn.Executing);
        holder.WorkingDir = "/home/act/dev/act";

        Blocking(card, holder).Should().NotBeNull();
    }

    [Fact]
    public void Nothing_is_blocked_while_the_guard_is_off()
        => WorkingDirConflict.Blocking(Ready(), [Held(BoardColumn.Executing)], Directories, enforced: false)
            .Should().BeNull();

    // The escape hatch, read off the card being started rather than the one already there.
    [Fact]
    public void An_exempt_card_launches_beside_the_card_holding_the_folder()
    {
        var card = Ready();
        card.AllowConcurrentWorkingDir = true;

        Blocking(card, Held(BoardColumn.Executing)).Should().BeNull();
    }

    [Fact]
    public void The_exemption_on_the_holder_does_not_let_anything_else_in()
    {
        var holder = Held(BoardColumn.Executing);
        holder.AllowConcurrentWorkingDir = true;

        Blocking(Ready(), holder).Should().NotBeNull();
    }

    // A retry and a re-attach belong to a session that already owns the folder. Guarding them would
    // strand the very card the folder is busy for.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void Only_a_ready_launch_is_guarded(BoardColumn column)
    {
        var card = Ready();
        card.Column = column;

        Blocking(card, Held(BoardColumn.Executing)).Should().BeNull();
    }

    [Fact]
    public void A_card_never_blocks_itself()
    {
        var card = Ready();

        WorkingDirConflict.Blocking(card, [card], Directories, enforced: true).Should().BeNull();
    }

    [Fact]
    public void A_card_with_no_working_directory_is_not_matched()
    {
        var card = Ready();
        card.WorkingDir = string.Empty;

        Blocking(card, Held(BoardColumn.Executing)).Should().BeNull();
    }

    // A task with no folder shares ACT's one scratch directory with every other one, so the guard has to
    // stand aside for it — otherwise the second quick question queues behind the first, which is the whole
    // thing the flag exists to prevent. Keyed on the flag rather than on the path being blank: a card that
    // still carries a typed folder must be exempt too, or the exemption depends on hygiene.
    [Fact]
    public void A_task_with_no_folder_is_blocked_by_nothing()
    {
        var asking = Ready();

        asking.NoWorkingDir = true;

        Blocking(asking, Held(BoardColumn.Executing)).Should().BeNull();
    }

    [Fact]
    public void A_task_with_no_folder_holds_nothing_against_anyone_else()
    {
        var holder = Held(BoardColumn.Executing);

        holder.NoWorkingDir = true;

        Blocking(Ready(), holder).Should().BeNull();
    }

    // The exemption survives a folder left in the field, which is the case a blank-path rule would miss.
    [Fact]
    public void A_no_folder_task_that_still_carries_a_path_is_exempt_anyway()
    {
        var asking = Ready();

        asking.NoWorkingDir = true;
        asking.WorkingDir = "/dev/act";

        Blocking(asking, Held(BoardColumn.Executing)).Should().BeNull();
    }

    // The queue runner's half. `Blocking` answers no about a card that is not Executing yet, so a pass
    // holding two quick questions is judged here — and it has to launch both.
    [Fact]
    public void Two_no_folder_tasks_in_one_pass_are_not_the_same_folder()
    {
        var first = Ready();
        var second = Held(BoardColumn.Ready);

        first.NoWorkingDir = true;
        second.NoWorkingDir = true;

        WorkingDirConflict.SameFolder(first, second, Directories).Should().BeFalse();
    }

    [Fact]
    public void One_no_folder_task_never_collides_with_a_real_folder()
    {
        var asking = Ready();

        asking.NoWorkingDir = true;

        WorkingDirConflict.SameFolder(asking, Held(BoardColumn.Ready), Directories).Should().BeFalse();
        WorkingDirConflict.SameFolder(Held(BoardColumn.Ready), asking, Directories).Should().BeFalse();
    }

    private static Card? Blocking(Card card, params Card[] others)
        => WorkingDirConflict.Blocking(card, others, Directories, enforced: true);

    private static Card Ready() => new()
    {
        Title = "The next task",
        Column = BoardColumn.Ready,
        WorkingDir = "/dev/act",
    };

    private static Card Held(BoardColumn column) => new()
    {
        Number = 1039,
        Title = "Already working",
        Column = column,
        WorkingDir = "/dev/act",
    };

    private static IWorkingDirectories Directories { get; } = new FakeDirectories();

    // Enough of the port to answer the only question the rule asks it, and deterministic on either
    // OS — the real implementation's answer depends on the machine's home directory and separator.
    private sealed class FakeDirectories : IWorkingDirectories
    {
        public string Home => "/home/act";

        public string Resolve(string workingDir)
        {
            var path = workingDir.Trim().Replace('\\', '/');

            if (path.StartsWith("~/", StringComparison.Ordinal))
                path = $"{Home}/{path[2..]}";

            return path == "~" ? Home : path;
        }

        public bool Exists(string workingDir) => true;

        public void Create(string workingDir) { }

        public PathCheck Check(string workingDir) => PathCheck.Found(Resolve(workingDir));

        public string NearestDirectory(string? path) => path is null ? Home : Resolve(path);

        public DirectoryListing List(string? path, bool includeFiles = false)
            => new(path, null, []);
    }
}
