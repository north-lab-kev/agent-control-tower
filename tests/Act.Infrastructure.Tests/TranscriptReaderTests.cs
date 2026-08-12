using System.Text;
using Act.Core.Abstractions;
using Act.Infrastructure.Transcripts;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public class TranscriptReaderTests
{
    private readonly ITranscriptReader reader = new TranscriptReader();

    [Fact]
    public void A_first_read_takes_the_whole_file()
    {
        using var transcript = new Transcript("one\ntwo\n");

        var read = reader.Read(transcript.Path, 0);

        read.Lines.Should().Equal(["one", "two"]);
        read.Offset.Should().Be(8);
    }

    [Fact]
    public void A_later_read_takes_only_what_was_appended()
    {
        using var transcript = new Transcript("one\n");

        var first = reader.Read(transcript.Path, 0);

        transcript.Append("two\nthree\n");

        var second = reader.Read(transcript.Path, first.Offset);

        second.Lines.Should().Equal(["two", "three"]);
    }

    // The whole reason the offset is returned rather than taken from the file's length: half a line
    // is not json, and the agent writes a line at a time.
    [Fact]
    public void A_line_still_being_written_is_left_for_the_next_read()
    {
        using var transcript = new Transcript("one\n{ \"partial\":");

        var first = reader.Read(transcript.Path, 0);

        first.Lines.Should().Equal(["one"]);
        first.Offset.Should().Be(4);

        transcript.Append(" 1 }\n");

        reader.Read(transcript.Path, first.Offset).Lines.Should().Equal(["{ \"partial\": 1 }"]);
    }

    [Fact]
    public void Nothing_appended_reads_nothing_and_holds_the_offset()
    {
        using var transcript = new Transcript("one\n");

        var first = reader.Read(transcript.Path, 0);
        var second = reader.Read(transcript.Path, first.Offset);

        second.Lines.Should().BeEmpty();
        second.Offset.Should().Be(first.Offset);
    }

    // A file that shrank was replaced rather than appended to, so the offset is meaningless and the
    // read starts over. Reading from the old offset would return the middle of a line.
    [Fact]
    public void A_file_that_shrank_is_read_from_the_start()
    {
        using var transcript = new Transcript("one\ntwo\nthree\n");

        var first = reader.Read(transcript.Path, 0);

        transcript.Replace("fresh\n");

        var read = reader.Read(transcript.Path, first.Offset);

        read.Lines.Should().Equal(["fresh"]);
        read.Restarted.Should().BeTrue("the tail's fold state describes a file that is gone");
    }

    // The restart must be reported even when the replacement holds nothing readable yet, or the
    // tail would learn of it only after the reset stopped being visible.
    [Fact]
    public void A_file_that_shrank_to_nothing_still_reports_the_restart()
    {
        using var transcript = new Transcript("one\ntwo\nthree\n");

        var first = reader.Read(transcript.Path, 0);

        transcript.Replace(string.Empty);

        var read = reader.Read(transcript.Path, first.Offset);

        read.Lines.Should().BeEmpty();
        read.Restarted.Should().BeTrue();
        read.Offset.Should().Be(0);
    }

    [Fact]
    public void An_ordinary_append_is_not_a_restart()
    {
        using var transcript = new Transcript("one\n");

        var first = reader.Read(transcript.Path, 0);

        transcript.Append("two\n");

        reader.Read(transcript.Path, first.Offset).Restarted.Should().BeFalse();
    }

    // The path arrives from a hook payload, and a card can be read before the CLI has created the
    // file. Silence, not an exception.
    [Fact]
    public void A_missing_file_reads_nothing()
    {
        using var directory = new TempDirectory();

        var read = reader.Read(Path.Combine(directory.Path, "nope.jsonl"), 0);

        read.Lines.Should().BeEmpty();
        read.Offset.Should().Be(0);
    }

    [Fact]
    public void Windows_line_endings_do_not_reach_the_parser()
    {
        using var transcript = new Transcript("one\r\ntwo\r\n");

        reader.Read(transcript.Path, 0).Lines.Should().Equal(["one", "two"]);
    }

    // The reader has to work while the agent still holds the file open for writing, which is the
    // normal case and not an edge one.
    [Fact]
    public void A_file_the_writer_still_holds_open_is_readable()
    {
        using var transcript = new Transcript("one\n");
        using var held = new FileStream(
            transcript.Path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);

        held.Write(Encoding.UTF8.GetBytes("two\n"));
        held.Flush();

        reader.Read(transcript.Path, 0).Lines.Should().Equal(["one", "two"]);
    }

    private sealed class Transcript : IDisposable
    {
        private readonly TempDirectory directory = new();

        public Transcript(string content)
        {
            Directory.CreateDirectory(directory.Path);

            Path = System.IO.Path.Combine(directory.Path, "session.jsonl");

            Replace(content);
        }

        public string Path { get; }

        public void Append(string content) => File.AppendAllText(Path, content, Encoding.UTF8);

        public void Replace(string content) => File.WriteAllText(Path, content, new UTF8Encoding(false));

        public void Dispose() => directory.Dispose();
    }
}
