using Act.Agents.Codex;
using Act.Core.Abstractions;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The inference that stands in for the hook Codex never fires. Every assertion here is a claim about a
// *convention*, which is why the whole class disappears the day a payload carries the path instead.
public class CodexRolloutFinderTests
{
    private static readonly DateTimeOffset Launch = new(2026, 7, 30, 2, 38, 40, TimeSpan.Zero);

    [Fact]
    public void A_rollout_started_after_the_launch_in_the_launch_directory_is_the_one()
    {
        var finder = Finder(
            ("2026/07/30/rollout-a.jsonl", Meta("019f-aaaa", @"C:\dev\act", "2026-07-30T02:38:41Z")));

        var found = finder.Locate(Search(@"C:\dev\act"));

        found!.Path.Should().EndWith("rollout-a.jsonl");
        found.SessionId.Should().Be("019f-aaaa");
    }

    // Binding is the whole point: there is no `--session-id` to pre-mint and no `SessionStart` payload
    // to read, so the file's own meta line is the only place a Codex session id exists.
    [Fact]
    public void The_session_id_comes_from_the_meta_line()
        => Finder(("r.jsonl", Meta("019f-bbbb", @"C:\dev\act", "2026-07-30T02:38:41Z")))
            .Locate(Search(@"C:\dev\act"))!
            .SessionId.Should().Be("019f-bbbb");

    [Fact]
    public void A_rollout_from_another_directory_is_not_it()
        => Finder(("r.jsonl", Meta("019f-cccc", @"C:\dev\other", "2026-07-30T02:38:41Z")))
            .Locate(Search(@"C:\dev\act"))
            .Should().BeNull();

    // Codex reports the cwd as it resolved it, which is not always spelled the way ACT stored it.
    [Fact]
    public void Separators_and_a_trailing_slash_do_not_make_it_another_directory()
        => Finder(("r.jsonl", Meta("019f-dddd", "C:/dev/act/", "2026-07-30T02:38:41Z")))
            .Locate(Search(@"C:\dev\act"))
            .Should().NotBeNull();

    // The session the user ran by hand five minutes ago is in the same folder and the same directory.
    // Only its start time tells it from the one ACT just launched.
    [Fact]
    public void A_rollout_that_started_before_the_launch_is_not_it()
        => Finder(("r.jsonl", Meta("019f-eeee", @"C:\dev\act", "2026-07-30T02:20:00Z")))
            .Locate(Search(@"C:\dev\act"))
            .Should().BeNull();

    // The launch stamp and the file's stamp are taken by different clocks, so a second either way
    // cannot be allowed to lose the file.
    [Fact]
    public void A_rollout_a_moment_before_the_launch_stamp_still_counts()
        => Finder(("r.jsonl", Meta("019f-ffff", @"C:\dev\act", "2026-07-30T02:38:39Z")))
            .Locate(Search(@"C:\dev\act"))
            .Should().NotBeNull();

    // Two cards launched together in one directory. Without this, the second card binds to the first
    // card's session and both terminals report the same numbers.
    [Fact]
    public void A_file_another_card_already_holds_is_not_a_candidate()
    {
        var finder = Finder(
            ("first.jsonl", Meta("019f-1111", @"C:\dev\act", "2026-07-30T02:38:41Z")),
            ("second.jsonl", Meta("019f-2222", @"C:\dev\act", "2026-07-30T02:38:42Z")));

        var found = finder.Locate(Search(@"C:\dev\act", claimed: "first.jsonl"));

        found!.SessionId.Should().Be("019f-2222");
    }

    // Oldest first among the candidates, so two launches take the files in the order they were made.
    [Fact]
    public void The_earliest_unclaimed_match_wins()
    {
        var finder = Finder(
            ("first.jsonl", Meta("019f-1111", @"C:\dev\act", "2026-07-30T02:38:41Z")),
            ("second.jsonl", Meta("019f-2222", @"C:\dev\act", "2026-07-30T02:38:42Z")));

        finder.Locate(Search(@"C:\dev\act"))!.SessionId.Should().Be("019f-1111");
    }

    // The mis-binding this class exists to prevent, and it happened for real: a card relaunched after
    // other sessions had run in the same directory bound itself to one of *those*, and reported its
    // tokens and turns. The launch stamp is what separates them, so it has to be this session's spawn
    // rather than the card's first-ever launch.
    [Fact]
    public void A_rollout_from_a_session_that_ran_between_launches_is_not_it()
    {
        var finder = Finder(
            ("mine.jsonl", Meta("019f-mine", @"C:\dev\act", "2026-07-30T02:38:41Z")),
            ("someone-elses.jsonl", Meta("019f-else", @"C:\dev\act", "2026-07-30T02:20:00Z")));

        finder.Locate(Search(@"C:\dev\act"))!.SessionId.Should().Be("019f-mine");
    }

    // A rollout that names no start time cannot be shown to be this launch's.
    [Fact]
    public void A_rollout_with_no_timestamp_is_not_it()
        => Finder(("r.jsonl", """{ "type": "session_meta", "payload": { "session_id": "x", "cwd": "C:\\dev\\act" } }"""))
            .Locate(Search(@"C:\dev\act"))
            .Should().BeNull();

    // A session already bound — a restore — is matched by identity, not by guessing. Its rollout is as
    // old as the session is, so every time and directory filter would throw it away.
    [Fact]
    public void A_known_session_id_matches_its_own_rollout_however_old()
    {
        var finder = Finder(
            ("rollout-2026-07-29T01-00-00-019f-old.jsonl", Meta("019f-old", @"C:\dev\somewhere-else", "2026-07-29T01:00:00Z")),
            ("rollout-2026-07-30T02-38-41-019f-new.jsonl", Meta("019f-new", @"C:\dev\act", "2026-07-30T02:38:41Z")));

        var found = finder.Locate(Search(@"C:\dev\act") with { SessionId = "019f-old" });

        found!.Path.Should().EndWith("019f-old.jsonl");
        found.SessionId.Should().Be("019f-old");
    }

    [Fact]
    public void A_known_session_id_with_no_rollout_yet_finds_nothing()
        => Finder(("rollout-019f-other.jsonl", Meta("019f-other", @"C:\dev\act", "2026-07-30T02:38:41Z")))
            .Locate(Search(@"C:\dev\act") with { SessionId = "019f-missing" })
            .Should().BeNull();

    [Fact]
    public void No_sessions_folder_yet_is_silence_rather_than_a_failure()
        => Finder().Locate(Search(@"C:\dev\act")).Should().BeNull();

    [Fact]
    public void A_file_whose_first_line_is_not_a_meta_line_is_skipped()
        => Finder(("r.jsonl", """{ "type": "event_msg", "payload": { "type": "task_started" } }"""))
            .Locate(Search(@"C:\dev\act"))
            .Should().BeNull();

    [Fact]
    public void An_unparseable_file_is_skipped()
        => Finder(("r.jsonl", "{ not json")).Locate(Search(@"C:\dev\act")).Should().BeNull();

    private static string Meta(string sessionId, string cwd, string timestamp)
        => $$"""
            { "timestamp": "{{timestamp}}", "type": "session_meta",
              "payload": { "session_id": "{{sessionId}}", "cwd": "{{cwd.Replace("\\", "\\\\")}}" } }
            """;

    private static TranscriptSearch Search(string workingDir, params string[] claimed)
        => new(workingDir, Launch, claimed.ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static CodexRolloutFinder Finder(params (string Path, string Head)[] files)
    {
        var store = new FakeFiles(files);

        return new CodexRolloutFinder(store, store);
    }

    // Stands in for both file ports at once, because the finder's whole job is the pairing: ask the
    // directory what is there, then ask the reader what each one says.
    private sealed class FakeFiles(
        (string Path, string Head)[] files) : ITranscriptDirectory, ITranscriptReader
    {
        // Newest first, as the real directory returns them — the finder is what reverses it.
        public IReadOnlyList<string> Newest(string directory, string pattern, int take)
            => [.. files.Select(file => file.Path).Reverse().Take(take)];

        public TranscriptRead Read(string path, long offset)
            => files.FirstOrDefault(file => file.Path == path) is { Head: { } head }
                ? new TranscriptRead(head.Length, [head])
                : TranscriptRead.Nothing(offset);
    }
}
