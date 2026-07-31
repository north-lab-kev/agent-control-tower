namespace Act.Core.Abstractions;

// Reads whole lines out of a transcript the agent still has open. A port for the same reason
// `IAgentConfigFiles` is one: touching the file system is an infrastructure fact, and the tail has
// to be testable without a file anywhere.
public interface ITranscriptReader
{
    TranscriptRead Read(string path, long offset);
}

// `Offset` is where the next read starts, which is not the same as the file's length: a line the
// agent is only half way through writing is left behind rather than parsed, so the offset advances
// to the last complete line and no further.
public sealed record TranscriptRead(long Offset, IReadOnlyList<string> Lines)
{
    public static TranscriptRead Nothing(long offset) => new(offset, []);
}
