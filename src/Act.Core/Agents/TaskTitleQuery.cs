using Act.Core.Resources;

namespace Act.Core.Agents;

// What ACT asks an agent when it wants a title, and what it will accept back. Both halves are pure,
// which is the point: the interesting failure is not the process, it is a CLI that answers with a
// paragraph, a markdown heading, a pair of quotes, or a cheerful "Sure! Here's a title:" — and every
// one of those is fixable without running anything.
//
// Localised like every other string ACT writes to an agent, for the reason `AutoGitInstruction`
// gives: the agent is addressed in the language the user runs ACT in, so a French board does not
// fill up with English titles.
public static class TaskTitleQuery
{
    // The ceiling, not the target — the prompt asks for far shorter than this, and a title that
    // arrives at the limit has already ignored the instruction. It matches the form's own
    // `MaxLength`, so a generated title is never something the user could not have typed.
    public const int MaxLength = 200;

    public const int MaxWords = 30;

    public static string PromptFor(string initialPrompt)
        => $"{CoreStrings.Title_Instruction}\n\n{initialPrompt.Trim()}";

    // Everything a chatty CLI wraps an answer in, taken off in one pass. The first non-empty line is
    // taken because a model that explains itself does so *after* the answer far more often than
    // before it, and because a title is one line by definition.
    public static string? Clean(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;

        var line = answer
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(candidate => Unwrap(candidate))
            .FirstOrDefault(candidate => candidate.Length > 0);

        return string.IsNullOrEmpty(line) ? null : Shorten(line);
    }

    // A local title, for when there is no answer to clean. Not a placeholder and not an apology: the
    // opening words of the prompt are what the user would most likely have typed themselves, and a
    // board full of those beats a save that failed over a field the user did not want to fill in.
    //
    // It answers for **any** prompt with a character in it, and that is the load-bearing part rather
    // than a nicety: it is the last thing standing between a failed query and an untitled card. So
    // where unwrapping empties the line — a prompt that is nothing but `###`, or a pair of quotes —
    // the line is taken as it stands instead. Ugly beats nameless.
    public static string FromPrompt(string initialPrompt)
    {
        var line = FirstLine(initialPrompt);

        return Unwrap(line) is { Length: > 0 } unwrapped ? Shorten(unwrapped) : Shorten(line);
    }

    private static string FirstLine(string text)
        => text
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0)
            ?? string.Empty;

    // Four passes, and the order is the point: the one-sided marks (a heading's `#`, a bullet's `-`)
    // have to go before the symmetrical ones, or `- **Title**` leaves its trailing asterisks behind.
    private static string Unwrap(string line)
    {
        var trimmed = line
            .Trim()
            .Trim('`')
            .TrimStart('#', '>', '-', '•', ' ')
            .Trim('*', '_', ' ')
            .Trim();

        while (trimmed.Length > 1 && Paired(trimmed[0], trimmed[^1]))
            trimmed = trimmed[1..^1].Trim();

        // Only a trailing period: a title that genuinely ends in "?" or "!" is saying something, and
        // one ending in a colon has had its subject cut off and should keep the evidence.
        return trimmed.TrimEnd('.').Trim();
    }

    private static bool Paired(char open, char close) => (open, close) switch
    {
        ('"', '"') or ('\'', '\'') or ('“', '”') or ('«', '»') or ('(', ')') or ('[', ']') => true,
        _ => false,
    };

    // Words first and characters second, because the two limits fail differently: a wordy answer is
    // a model ignoring the brief, and a single 300-character "word" is a model pasting a path. The
    // character cut is hard and unapologetic — a truncated title is still a usable one, and the user
    // can edit it.
    private static string Shorten(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var shortened = words.Length > MaxWords
            ? string.Join(' ', words[..MaxWords])
            : string.Join(' ', words);

        return shortened.Length > MaxLength ? shortened[..MaxLength].TrimEnd() : shortened;
    }
}
