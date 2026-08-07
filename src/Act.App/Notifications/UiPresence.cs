using System.Collections.Concurrent;

namespace Act.App.Notifications;

public sealed class UiPresence
{
    private readonly ConcurrentDictionary<Guid, bool> watchers = new();

    public bool BoardIsBeingWatched => watchers.Values.Any(watching => watching);

    public void Report(Guid watcher, bool focused, string route)
        => watchers[watcher] = focused && IsBoard(route);

    public void Forget(Guid watcher) => watchers.TryRemove(watcher, out _);

    private static bool IsBoard(string route)
    {
        var path = route.Split('?', '#')[0].Trim('/');

        return path.Length == 0;
    }
}
