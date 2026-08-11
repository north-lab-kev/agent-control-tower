using System.Diagnostics;

namespace Act.Core.Telemetry;

// Everything a crash report may say about an exception, and nothing else.
//
// The line that matters: **the exception's own text never leaves, but our source does.** A `Message`
// is runtime data from the user's machine and in this app it routinely names the file that failed —
// a transcript, an attachment, a working directory. A file *name* and a line number are facts about
// ACT's source, read from the PDB that ships beside the binary; they describe this repository, not
// anything of the user's. The promise on the switch is about their paths, not ours.
//
// Only the base name is emitted, never `abs_path`. The absolute path is the build machine's and says
// nothing a reader needs — and `TelemetryPayload.IsSymbol` rejects separators anyway, so a full path
// would be dropped on the way out rather than sent.
public static class TelemetryFault
{
    public const int MostFrames = 12;

    // An exception nested deeper than this is pathological, and walking it forever is how a crash
    // handler becomes the crash.
    private const int MostNested = 5;

    // The exception that actually failed. A wrapper is what the handler catches — `AggregateException`
    // from an unobserved task, a `TargetInvocationException` from reflection — and reporting its type
    // buries every distinct failure under one useless heading. This unwraps to the innermost, which
    // is what `exception` reports and what the issue list should be grouped by.
    public static Exception Failure(Exception error) => Nested(error).Last();

    // Outermost first, so a reader can still see it was an `AggregateException` that carried it.
    public static string[] Chain(Exception error)
        => [.. Nested(error).Select(nested => nested.GetType().FullName ?? nested.GetType().Name)];

    // Taken from the innermost exception that has any, falling outward. A wrapper usually carries no
    // stack of its own — reading only the outermost would leave a crash report with no frames at all.
    public static string[] Frames(Exception error)
    {
        foreach (var nested in Nested(error).Reverse())
        {
            var frames = Of(nested);

            if (frames.Length > 0)
                return frames;
        }

        return [];
    }

    private static string[] Of(Exception error)
    {
        var trace = new StackTrace(error, fNeedFileInfo: true);

        return
        [
            .. trace.GetFrames()
                .Select(Frame)
                .Where(frame => frame.Length > 0)
                .Take(MostFrames),
        ];
    }

    private static IEnumerable<Exception> Nested(Exception error)
    {
        var current = (Exception?)error;

        for (var depth = 0; current is not null && depth < MostNested; depth++)
        {
            yield return current;

            current = current is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.FirstOrDefault()
                : current.InnerException;
        }
    }

    private static string Frame(StackFrame frame)
    {
        if (frame.GetMethod() is not { } method)
            return string.Empty;

        var owner = method.DeclaringType?.FullName;
        var name = owner is null ? method.Name : $"{owner}.{method.Name}";

        return Capped(name + Where(frame));
    }

    private static string Where(StackFrame frame)
    {
        if (frame.GetFileName() is not { Length: > 0 } path)
            return string.Empty;

        var file = Path.GetFileName(path);
        var line = frame.GetFileLineNumber();

        return line > 0 ? $" ({file}:{line})" : $" ({file})";
    }

    private static string Capped(string frame)
        => frame.Length > TelemetryPayload.LongestSymbol
            ? frame[..TelemetryPayload.LongestSymbol]
            : frame;
}
