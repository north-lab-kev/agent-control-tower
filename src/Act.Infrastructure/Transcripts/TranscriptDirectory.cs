using Act.Core.Abstractions;

namespace Act.Infrastructure.Transcripts;

// Newest-first by last write rather than by name: a rollout's filename carries a *local* timestamp
// while everything ACT compares against is UTC, so the name is not something to sort or reason about.
public sealed class TranscriptDirectory : ITranscriptDirectory
{
    public IReadOnlyList<string> Newest(string directory, string pattern, int take)
    {
        if (!Directory.Exists(directory))
            return [];

        return
        [
            .. new DirectoryInfo(directory)
                .EnumerateFiles(pattern, SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(take)
                .Select(file => file.FullName),
        ];
    }
}
