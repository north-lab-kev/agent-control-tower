using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Logging;

public static class ActLogScope
{
    private const string TaskField = "Task";

    private const string SessionField = "Session";

    public static IDisposable? BeginTaskScope(this ILogger log, int? task = null, string? session = null)
    {
        var fields = new Dictionary<string, object?>(2);

        if (task is { } number)
            fields[TaskField] = number;

        if (!string.IsNullOrEmpty(session))
            fields[SessionField] = session;

        return fields.Count == 0 ? null : log.BeginScope(fields);
    }
}
