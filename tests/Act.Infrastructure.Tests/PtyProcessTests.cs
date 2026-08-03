using System.Reflection;
using System.Text;
using Act.Infrastructure.Terminal;
using AwesomeAssertions;
using Porta.Pty;

namespace Act.Infrastructure.Tests;

// The byte pipeline every session runs through, and until 2026-08-02 it had no tests at all — which
// is where a silent-terminal-death bug lived (see *the decoder* below). Nothing here needs a real
// process: `PtyProcess` takes `IPtyConnection`, so a fake stream is the whole harness.
public class PtyProcessTests
{
    // The regression test for the bug. A UTF-8 sequence can straddle two reads, so the decoder
    // carries the tail — and a 4-byte sequence completed by the *first* byte of the next read emits
    // a surrogate **pair**: two chars for one byte. Sized at the byte count the char buffer
    // overflows by exactly one, `GetChars` throws `ArgumentException` — which is not an
    // `IOException`, so the read loop died and that session's terminal went blank for good with
    // nothing logged.
    [Fact]
    public async Task A_surrogate_pair_completed_by_a_full_read_does_not_overflow_the_decoder()
    {
        var emoji = Encoding.UTF8.GetBytes("😀");

        // Three of the four bytes, so the decoder is holding a tail...
        var first = emoji[..3];

        // ...and the next read is a *full* buffer whose first byte completes it: 2 chars from that
        // one byte, plus 8191 more. 8193 characters out of 8192 bytes in.
        var second = new byte[8192];
        second[0] = emoji[3];
        Array.Fill(second, (byte)'x', 1, second.Length - 1);

        var (text, _) = await PumpAsync(first, second);

        text.Should().StartWith("😀");
        text.Length.Should().Be(8193);
        text.Should().NotContain("�", "a replacement character means the sequence was mangled");
    }

    [Fact]
    public async Task A_character_split_across_two_reads_arrives_whole()
    {
        var emoji = Encoding.UTF8.GetBytes("😀");

        var (text, _) = await PumpAsync(emoji[..2], emoji[2..]);

        text.Should().Be("😀");
    }

    [Fact]
    public async Task Output_arrives_batched_rather_than_once_per_read()
    {
        var reads = Enumerable.Range(0, 20).Select(i => Encoding.UTF8.GetBytes($"chunk{i} ")).ToArray();

        var (text, batches) = await PumpAsync(reads);

        text.Should().Be(string.Concat(reads.Select(Encoding.UTF8.GetString)));

        // A full-screen TUI redraws far more often than a Blazor circuit wants to be poked.
        batches.Should().BeLessThan(reads.Length);
    }

    [Fact]
    public async Task The_process_id_comes_from_the_connection()
    {
        var connection = new FakeConnection();

        await using var process = Started(connection);

        process.ProcessId.Should().Be(FakeConnection.FakePid);
    }

    [Fact]
    public async Task An_exit_is_reported_with_its_code()
    {
        var connection = new FakeConnection();
        var codes = new List<int>();

        await using var process = Started(connection);

        process.Exited += code => codes.Add(code);
        connection.RaiseExited(3);

        codes.Should().Equal(3);
    }

    // Load-bearing for `PtyAgentSession`: a teardown ACT asked for — a sign-off, a terminal restart
    // — must not come back as an exit the card would then wear as `error`.
    [Fact]
    public async Task An_exit_after_disposal_is_not_reported()
    {
        var connection = new FakeConnection();
        var codes = new List<int>();

        var process = Started(connection);
        process.Exited += code => codes.Add(code);

        await process.DisposeAsync();
        connection.RaiseExited(1);

        codes.Should().BeEmpty();
    }

    // Killing the agent is not enough: the pseudo-console is a separate object owning its own
    // `conhost.exe` and outlives the process attached to it, so skipping the connection's own
    // disposal leaks one conhost per session ACT ever launches.
    [Fact]
    public async Task Disposal_kills_the_process_and_disposes_the_pseudo_console()
    {
        var connection = new FakeConnection();

        await Started(connection).DisposeAsync();

        connection.Killed.Should().BeTrue();
        connection.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        var connection = new FakeConnection();
        var process = Started(connection);

        await process.DisposeAsync();
        await process.DisposeAsync();

        connection.KillCount.Should().Be(1);
    }

    [Fact]
    public async Task Keystrokes_reach_the_writer_as_utf8()
    {
        var connection = new FakeConnection();

        await using var process = Started(connection);

        await process.WriteAsync("héllo 😀");

        connection.Written.Should().Equal(Encoding.UTF8.GetBytes("héllo 😀"));
    }

    [Fact]
    public async Task Nothing_is_written_after_disposal()
    {
        var connection = new FakeConnection();
        var process = Started(connection);

        await process.DisposeAsync();
        await process.WriteAsync("too late");

        connection.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_write_is_not_sent()
    {
        var connection = new FakeConnection();

        await using var process = Started(connection);

        await process.WriteAsync(string.Empty);

        connection.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resize_reaches_the_connection()
    {
        var connection = new FakeConnection();

        await using var process = Started(connection);

        process.Resize(120, 40);

        connection.Resizes.Should().Equal((120, 40));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(120, 0)]
    [InlineData(-1, -1)]
    public async Task A_nonsense_size_is_refused(int cols, int rows)
    {
        var connection = new FakeConnection();

        await using var process = Started(connection);

        process.Resize(cols, rows);

        connection.Resizes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resize_after_disposal_is_ignored()
    {
        var connection = new FakeConnection();
        var process = Started(connection);

        await process.DisposeAsync();
        process.Resize(120, 40);

        connection.Resizes.Should().BeEmpty();
    }

    // A pty that has already gone is a normal thing to meet on the way out, not a crash.
    [Fact]
    public async Task A_resize_a_dead_pty_refuses_is_swallowed()
    {
        var connection = new FakeConnection { ResizeThrows = new InvalidOperationException("gone") };

        await using var process = Started(connection);

        process.Resize(120, 40);
    }

    [Fact]
    public async Task A_kill_that_throws_does_not_stop_disposal()
    {
        var connection = new FakeConnection { KillThrows = new InvalidOperationException("already dead") };

        await Started(connection).DisposeAsync();

        connection.Disposed.Should().BeTrue();
    }

    private static PtyProcess Started(FakeConnection connection)
    {
        var process = new PtyProcess(connection);

        process.Start();

        return process;
    }

    // Feeds the reads in, waits for the flusher to drain them, and hands back what a view would have
    // been shown plus how many batches it took.
    private static async Task<(string Text, int Batches)> PumpAsync(params byte[][] reads)
    {
        var connection = new FakeConnection(reads);
        var seen = new StringBuilder();
        var batches = 0;

        await using var process = Started(connection);

        process.Output += chunk =>
        {
            lock (seen)
            {
                seen.Append(chunk);
                batches++;
            }

            return Task.CompletedTask;
        };

        var expected = reads.Sum(read => read.Length);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            lock (seen)
            {
                if (Encoding.UTF8.GetByteCount(seen.ToString()) >= expected)
                    break;
            }

            await Task.Delay(10);
        }

        lock (seen)
            return (seen.ToString(), batches);
    }

    private sealed class FakeConnection(params byte[][] reads) : IPtyConnection
    {
        public const int FakePid = 4242;

        private readonly MemoryStream writer = new();

        public Stream ReaderStream { get; } = new QueuedStream(reads);

        public Stream WriterStream => writer;

        public int Pid => FakePid;

        public int ExitCode => 0;

        public bool Killed => KillCount > 0;

        public int KillCount { get; private set; }

        public bool Disposed { get; private set; }

        public List<(int Cols, int Rows)> Resizes { get; } = [];

        public byte[] Written => writer.ToArray();

        public Exception? ResizeThrows { get; init; }

        public Exception? KillThrows { get; init; }

        public event EventHandler<PtyExitedEventArgs>? ProcessExited;

        public void RaiseExited(int exitCode) => ProcessExited?.Invoke(this, ExitedArgs(exitCode));

        public bool WaitForExit(int milliseconds) => true;

        public void Kill()
        {
            KillCount++;

            if (KillThrows is { } failure)
                throw failure;
        }

        public void Resize(int cols, int rows)
        {
            if (ResizeThrows is { } failure)
                throw failure;

            Resizes.Add((cols, rows));
        }

        public void Dispose() => Disposed = true;

        // `PtyExitedEventArgs` has an internal constructor, so the only way a test can raise the
        // event the way the real transport does is to reach for it.
        private static PtyExitedEventArgs ExitedArgs(int exitCode)
            => (PtyExitedEventArgs)Activator.CreateInstance(
                typeof(PtyExitedEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [exitCode],
                culture: null)!;
    }

    // Hands back one queued chunk per read, then end-of-stream — so the read loop sees exactly the
    // boundaries the test chose, which is the whole point when the bug is about a split sequence.
    private sealed class QueuedStream(byte[][] reads) : Stream
    {
        private int next;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (next >= reads.Length)
                return ValueTask.FromResult(0);

            var chunk = reads[next++];

            chunk.CopyTo(buffer[..chunk.Length]);

            return ValueTask.FromResult(chunk.Length);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
