using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActDatabase
{
    private const string FileName = "act.db";

    public static ILiteDatabase Open(string dataDirectory)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory));

        var database = new LiteDatabase(Path.Combine(directory.FullName, FileName), ActBsonMapper.Create());

        try
        {
            ActSchema.Apply(database);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }
}
