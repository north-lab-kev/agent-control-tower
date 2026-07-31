namespace Act.Core.Abstractions;

// Where an agent keeps its transcripts, for the one agent ACT has to go looking. Separate from
// `ITranscriptReader` because finding and tailing are different jobs, and the same reason both are
// ports at all: the file system belongs to infrastructure.
public interface ITranscriptDirectory
{
    // Paths under `directory` matching `pattern`, newest first, capped at `take`. A directory that
    // does not exist yields nothing — a CLI that has never run has no session folder.
    IReadOnlyList<string> Newest(string directory, string pattern, int take);
}
