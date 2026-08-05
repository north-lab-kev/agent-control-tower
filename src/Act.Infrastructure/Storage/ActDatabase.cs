using LiteDB;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure.Storage;

internal static class ActDatabase
{
    private const string FileName = "act.db";

    public static ILiteDatabase Open(string dataDirectory, ILogger log)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory));

        var mapper = ActBsonMapper.Create();

        var database = new LiteDatabase(Path.Combine(directory.FullName, FileName), mapper);

        try
        {
            ActSchema.Apply(database, mapper, log);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }
}
