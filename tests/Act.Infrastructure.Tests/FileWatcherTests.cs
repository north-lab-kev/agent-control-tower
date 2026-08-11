using System.Diagnostics;
using Act.Infrastructure.FileSystem;
using AwesomeAssertions;

namespace Act.Infrastructure.Tests;

public sealed class FileWatcherTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Briefly = TimeSpan.FromMilliseconds(250);

    private readonly string directory = Directory.CreateTempSubdirectory("act-file-watch-").FullName;

    private readonly FileWatcher watcher = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_write_to_the_watched_file_answers_true()
    {
        var path = PathOf("auth.json");
        File.WriteAllText(path, "old");

        using var watch = watcher.Watch(path);

        var waiting = watch.WaitForChangeAsync(Wait, TimeSpan.Zero);

        File.WriteAllText(path, "new");

        (await waiting).Should().BeTrue();
    }

    [Fact]
    public async Task A_write_to_another_file_answers_false()
    {
        var path = PathOf("auth.json");
        File.WriteAllText(path, "old");

        using var watch = watcher.Watch(path);

        var waiting = watch.WaitForChangeAsync(Briefly, TimeSpan.Zero);

        File.WriteAllText(PathOf("other.json"), "new");

        (await waiting).Should().BeFalse();
    }

    [Fact]
    public async Task A_file_created_after_the_watch_started_answers_true()
    {
        var path = PathOf("auth.json");

        using var watch = watcher.Watch(path);

        var waiting = watch.WaitForChangeAsync(Wait, TimeSpan.Zero);

        File.WriteAllText(path, "new");

        (await waiting).Should().BeTrue();
    }

    [Fact]
    public async Task A_change_seen_before_the_wait_started_is_not_lost()
    {
        var path = PathOf("auth.json");

        using var watch = watcher.Watch(path);

        File.WriteAllText(path, "new");

        (await watch.WaitForChangeAsync(Wait, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public async Task Reset_forgets_what_was_seen_before_it()
    {
        var path = PathOf("auth.json");
        File.WriteAllText(path, "old");

        using var watch = watcher.Watch(path);

        File.WriteAllText(path, "new");

        (await watch.WaitForChangeAsync(Wait, TimeSpan.Zero)).Should().BeTrue();

        await Task.Delay(100);

        watch.Reset();

        (await watch.WaitForChangeAsync(Briefly, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_directory_never_signals_and_waits_out_the_time()
    {
        using var watch = watcher.Watch(Path.Combine(directory, "missing", "auth.json"));

        (await watch.WaitForChangeAsync(Briefly, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public async Task The_floor_keeps_an_instant_change_from_answering_immediately()
    {
        var path = PathOf("auth.json");

        using var watch = watcher.Watch(path);

        File.WriteAllText(path, "new");

        var started = Stopwatch.GetTimestamp();

        (await watch.WaitForChangeAsync(Wait, TimeSpan.FromMilliseconds(300))).Should().BeTrue();

        Stopwatch.GetElapsedTime(started).TotalMilliseconds.Should().BeGreaterThanOrEqualTo(250);
    }

    [Fact]
    public async Task Cancellation_stops_the_wait()
    {
        var path = PathOf("auth.json");
        File.WriteAllText(path, "old");

        using var watch = watcher.Watch(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var waiting = async () => await watch.WaitForChangeAsync(Wait, TimeSpan.Zero, cancellation.Token);

        await waiting.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task Cancellation_stops_the_wait_on_a_missing_directory_too()
    {
        using var watch = watcher.Watch(Path.Combine(directory, "missing", "auth.json"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var waiting = async () => await watch.WaitForChangeAsync(Wait, TimeSpan.Zero, cancellation.Token);

        await waiting.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public void Disposing_a_watch_is_harmless_either_way()
    {
        var real = watcher.Watch(PathOf("auth.json"));
        var missing = watcher.Watch(Path.Combine(directory, "missing", "auth.json"));

        var dispose = () =>
        {
            real.Dispose();
            missing.Dispose();
        };

        dispose.Should().NotThrow();
    }

    private string PathOf(string name) => Path.Combine(directory, name);
}
