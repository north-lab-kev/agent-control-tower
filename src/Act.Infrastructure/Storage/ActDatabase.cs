using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActDatabase
{
    private const string FileName = "act.db";

    public static ILiteDatabase Open(string dataDirectory)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory));

        return new LiteDatabase(Path.Combine(directory.FullName, FileName));
    }
}
