using System.Text;

namespace Act.Infrastructure.Terminal;

// Windows has no argv. A process receives one command-line *string* and each runtime re-splits it,
// so whoever builds that string owns the escaping — and `Porta.Pty`'s own escaping doubles quotes
// (`"` → `""`), which Claude Code's parser does not read back as a literal quote. It truncates the
// argument there instead.
//
// Measured 2026-07-30, and the reason this file exists: ACT's injected preamble contains
// `{ "state": "ready_for_review" }`, so every launch was handing Claude Code an argument that ended
// at that first quote — 677 characters of preamble and **no task prompt at all**. The agent then
// greeted the user and ended its turn, which looked enough like success that the hooks, the
// transitions and the board all agreed nothing was wrong.
//
// The fix is to take the job over rather than to escape on top of it: `VerbatimCommandLine` tells
// the transport to pass the command line through untouched, and ACT quotes each argument by the
// rules `CommandLineToArgvW` documents — a quote is literal when preceded by a backslash, a run of
// backslashes is literal only when no quote follows (so each run before one is doubled, plus one for
// the quote), and trailing backslashes are doubled because the closing quote follows them. Measured
// against the real CLI: 226 characters in, 226 out, where the transport's own escaping gave 28.
public static class WindowsArgument
{
    // Only Windows re-splits a string. Elsewhere the arguments reach `exec` as a real array, where
    // both the quoting and the verbatim flag would be nonsense.
    public static bool NeedsQuoting => OperatingSystem.IsWindows();

    public static string Quote(string argument) => $"\"{Escape(argument)}\"";

    public static IReadOnlyList<string> ForCommandLine(IReadOnlyList<string> arguments)
        => NeedsQuoting ? [.. arguments.Select(Quote)] : arguments;

    public static string Escape(string argument)
    {
        if (argument.Length == 0 || argument.IndexOfAny(['"', '\\']) < 0)
            return argument;

        var builder = new StringBuilder(argument.Length + 8);
        var backslashes = 0;

        foreach (var character in argument)
        {
            switch (character)
            {
                case '\\':
                    backslashes++;

                    break;

                case '"':
                    builder.Append('\\', (2 * backslashes) + 1).Append('"');
                    backslashes = 0;

                    break;

                default:
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                    builder.Append(character);

                    break;
            }
        }

        // The closing quote is about to follow, so these have to be doubled or it would be escaped
        // instead — the classic way a path argument ending in a separator eats the quote.
        builder.Append('\\', 2 * backslashes);

        return builder.ToString();
    }
}
