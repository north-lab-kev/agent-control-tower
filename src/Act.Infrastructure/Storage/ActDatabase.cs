using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActDatabase
{
    private const string FileName = "act.db";

    public static ILiteDatabase Open(string dataDirectory)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory));

        var mapper = ActBsonMapper.Create();

        var database = new LiteDatabase(Path.Combine(directory.FullName, FileName), mapper);

        try
        {
            ActSchema.Apply(database, mapper);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }
}
