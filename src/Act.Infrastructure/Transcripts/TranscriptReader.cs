using System.Text;
using Act.Core.Abstractions;

namespace Act.Infrastructure.Transcripts;

// Tails a JSONL file its author still has open. Three things follow from that and none of them is
// optional: the share mode has to let the writer keep writing, a trailing partial line has to be
// left behind because half a line is not json, and a file that has *shrunk* was replaced rather
// than appended to, so the read starts over.
public sealed class TranscriptReader : ITranscriptReader
{
    private const char ByteOrderMark = '\uFEFF';

    public TranscriptRead Read(string path, long offset)
    {
        var file = new FileInfo(path);

        if (!file.Exists)
            return TranscriptRead.Nothing(offset);

        if (file.Length < offset)
            offset = 0;

        if (file.Length == offset)
            return TranscriptRead.Nothing(offset);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        stream.Seek(offset, SeekOrigin.Begin);

        var buffer = new byte[file.Length - offset];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        if (read <= 0)
            return TranscriptRead.Nothing(offset);

        var lastComplete = Array.LastIndexOf(buffer, (byte)'\n', read - 1);

        if (lastComplete < 0)
            return TranscriptRead.Nothing(offset);

        var text = Encoding.UTF8.GetString(buffer, 0, lastComplete + 1).TrimStart(ByteOrderMark);

        var lines = text.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new TranscriptRead(offset + lastComplete + 1, lines);
    }
}
