using System.Text;
using Act.Infrastructure.Hooks;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class AgentConfigFilesTests
{
    private const string Content = "{ \"hooks\": [] }";

    private static readonly Guid Task = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void A_task_file_lands_under_that_task_s_own_folder()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);

        var path = files.Write(Task, "settings.json", "{}");

        path.Should().Be(Path.Combine(
            temp.Path,
            AgentConfigFiles.DirectoryName,
            Task.ToString("d"),
            "settings.json"));

        File.ReadAllText(path).Should().Be("{}");
    }

    [Fact]
    public void A_shared_file_lands_beside_the_task_folders_rather_than_inside_one()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);

        files.WriteShared("config.toml", "[profiles]")
            .Should().Be(Path.Combine(temp.Path, AgentConfigFiles.DirectoryName, "config.toml"));
    }

    [Fact]
    public void Writing_creates_every_directory_on_the_way_down()
    {
        using var temp = new TempDirectory();

        Directory.Exists(temp.Path).Should().BeFalse("the fixture only names a directory");

        new AgentConfigFiles(temp.Path).Write(Task, "settings.json", "{}");

        File.Exists(Path.Combine(
            temp.Path,
            AgentConfigFiles.DirectoryName,
            Task.ToString("d"),
            "settings.json")).Should().BeTrue();
    }

    // The reason the encoding is pinned rather than left to the default: Codex's hooks parser
    // rejects a BOM outright with "expected value at line 1 column 1".
    [Fact]
    public void Nothing_is_written_with_a_byte_order_mark()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var external = Path.Combine(temp.Path, "external", "hooks.json");

        files.WriteExternal(external, Content);

        string[] written =
        [
            files.Write(Task, "settings.json", Content),
            files.WriteShared("config.toml", Content),
            external,
        ];

        foreach (var path in written)
            File.ReadAllBytes(path).Take(3).Should().NotEqual(Encoding.UTF8.GetPreamble(), path);
    }

    [Fact]
    public void The_scratch_directory_is_created_rather_than_only_named()
    {
        using var temp = new TempDirectory();

        var scratch = new AgentConfigFiles(temp.Path).ScratchDirectory();

        scratch.Should().Be(Path.Combine(temp.Path, AgentConfigFiles.ScratchName));
        Directory.Exists(scratch).Should().BeTrue("a CLI handed a missing working directory fails at spawn");
    }

    [Fact]
    public void The_scratch_directory_stays_empty_and_asking_twice_is_harmless()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);

        files.ScratchDirectory().Should().Be(files.ScratchDirectory());
        Directory.EnumerateFileSystemEntries(files.ScratchDirectory()).Should().BeEmpty();
    }

    [Fact]
    public void An_external_write_creates_the_folder_it_was_pointed_at()
    {
        using var temp = new TempDirectory();

        var path = Path.Combine(temp.Path, "home", ".codex", "config.toml");

        new AgentConfigFiles(temp.Path).WriteExternal(path, "[hooks]");

        File.ReadAllText(path).Should().Be("[hooks]");
    }

    [Fact]
    public void An_external_write_replaces_what_was_there()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, "first");
        files.WriteExternal(path, "second");

        File.ReadAllText(path).Should().Be("second");
    }

    // The point of the marker: ACT owns the head of the file and the user owns the tail, so a
    // rewrite must not take their section with it.
    [Fact]
    public void A_preserved_tail_survives_the_rewrite_from_the_marker_down()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, string.Join(
            Environment.NewLine,
            "[act]",
            "generated = 1",
            "[user]",
            "keep = true"));

        files.WriteExternalPreservingTail(path, "[act]", "[user]");

        File.ReadAllText(path).Should().Be(string.Join(
            Environment.NewLine,
            "[act]",
            "[user]",
            "keep = true"));
    }

    [Fact]
    public void A_marker_indented_by_the_user_is_still_the_start_of_the_tail()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, string.Join(Environment.NewLine, "[act]", "   [user]", "keep = true"));

        files.WriteExternalPreservingTail(path, "[act]", "[user]");

        File.ReadAllText(path).Should().Contain("keep = true");
    }

    // The shape Codex's own save produces, measured in `docs/findings/codex-hooks.md`: ACT's
    // `[mcp_servers.act]` table comes back *below* `[hooks.state]`, inside the tail the next launch
    // carries across. Carried verbatim it would sit under the fresh copy of itself, and a table
    // defined twice is a profile the parser rejects — every launch after the first save would die.
    [Fact]
    public void A_table_the_rewrite_defines_again_is_dropped_from_the_tail()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, string.Join(
            Environment.NewLine,
            "[[hooks.SessionStart]]",
            "matcher = \"*\"",
            "[hooks.state]",
            "[hooks.state.'C:\\\\act.config.toml:session_start:0:0']",
            "trusted_hash = \"sha256:8325\"",
            "[mcp_servers.act]",
            "url = \"http://127.0.0.1:49711/old\""));

        files.WriteExternalPreservingTail(
            path,
            string.Join(
                Environment.NewLine,
                "[[hooks.SessionStart]]",
                "matcher = \"*\"",
                "[mcp_servers.act]",
                "url = \"http://127.0.0.1:49712/new\""),
            "[hooks.state");

        var written = File.ReadAllText(path);

        written.Should().Contain("trusted_hash", "the CLI's trust state is what the tail exists to keep");
        written.Should().Contain("/new");
        written.Should().NotContain("/old", "the relocated copy of ACT's own table is stale");
        written.Split("[mcp_servers.act]").Should().HaveCount(2, "the table must be defined exactly once");
    }

    // The dropped block ends where the next table the CLI owns begins — trust state written after
    // ACT's relocated table must not be swallowed with it.
    [Fact]
    public void Dropping_a_stale_table_keeps_whatever_follows_it()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, string.Join(
            Environment.NewLine,
            "[act]",
            "generated = 1",
            "[user]",
            "before = true",
            "[act.owned]",
            "stale = true",
            "[user.after]",
            "after = true"));

        files.WriteExternalPreservingTail(
            path,
            string.Join(Environment.NewLine, "[act]", "[act.owned]", "fresh = true"),
            "[user]");

        var written = File.ReadAllText(path);

        written.Should().Contain("before = true").And.Contain("after = true").And.Contain("fresh = true");
        written.Should().NotContain("stale = true");
    }

    [Fact]
    public void A_file_with_no_marker_is_replaced_whole()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, "nothing of ours");

        files.WriteExternalPreservingTail(path, "[act]", "[user]");

        File.ReadAllText(path).Should().Be("[act]");
    }

    [Fact]
    public void A_first_write_with_no_file_to_preserve_is_just_a_write()
    {
        using var temp = new TempDirectory();

        var path = Path.Combine(temp.Path, "config.toml");

        new AgentConfigFiles(temp.Path).WriteExternalPreservingTail(path, "[act]", "[user]");

        File.ReadAllText(path).Should().Be("[act]");
    }

    [Fact]
    public void Deleting_an_external_file_removes_it()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, "[hooks]");
        files.DeleteExternal(path);

        File.Exists(path).Should().BeFalse();
    }

    // Every one of these runs on a teardown path, where the thing it is undoing may never have
    // happened. None of them may throw.
    [Fact]
    public void Deleting_a_file_that_is_not_there_is_silent()
    {
        using var temp = new TempDirectory();

        var act = () => new AgentConfigFiles(temp.Path)
            .DeleteExternal(Path.Combine(temp.Path, "gone", "config.toml"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Deleting_a_path_that_is_a_directory_is_swallowed()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var directory = files.ScratchDirectory();

        var act = () => files.DeleteExternal(directory);

        act.Should().NotThrow();
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public void Clearing_a_task_takes_its_folder_and_leaves_the_others()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var other = Guid.NewGuid();

        files.Write(Task, "settings.json", "{}");
        var kept = files.Write(other, "settings.json", "{}");

        files.Clear(Task);

        Directory.Exists(Path.Combine(temp.Path, AgentConfigFiles.DirectoryName, Task.ToString("d")))
            .Should().BeFalse();
        File.Exists(kept).Should().BeTrue();
    }

    [Fact]
    public void Clearing_a_task_that_wrote_nothing_is_silent()
    {
        using var temp = new TempDirectory();

        var act = () => new AgentConfigFiles(temp.Path).Clear(Guid.NewGuid());

        act.Should().NotThrow();
    }

    // The two halves are deliberately different. Failing to *read* the tail is survivable — the file
    // is rewritten whole and the user answers Codex's review screen again — so it is swallowed here.
    // Failing to *write* is not something this class can answer, so it propagates, and
    // `CodexAdapter.InjectHooks` is what turns it into a launch with no hooks rather than a lost task.
    //
    // Windows is the only platform that really enforces the share mode, so the assertion is scoped
    // to it rather than made conditional on nothing.
    [Fact]
    public void A_write_that_the_filesystem_refuses_reaches_the_caller_that_can_answer_it()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = Path.Combine(temp.Path, "config.toml");

        files.WriteExternal(path, string.Join(Environment.NewLine, "[act]", "[user]", "keep = true"));

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var write = () => files.WriteExternalPreservingTail(path, "[act]", "[user]");

        write.Should().Throw<IOException>();
    }

    [Fact]
    public void Clearing_a_task_whose_file_is_still_held_open_is_swallowed()
    {
        using var temp = new TempDirectory();

        var files = new AgentConfigFiles(temp.Path);
        var path = files.Write(Task, "settings.json", Content);

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var clear = () => files.Clear(Task);

        clear.Should().NotThrow();
    }
}
