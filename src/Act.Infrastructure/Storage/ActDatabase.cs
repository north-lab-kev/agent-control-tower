using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Infrastructure.Storage;

internal static class ActDatabase
{
    private const string FileName = "act.db";

    public static ILiteDatabase Open(string dataDirectory, ILogger? log = null)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory));

        var mapper = ActBsonMapper.Create();

        var database = new LiteDatabase(Path.Combine(directory.FullName, FileName), mapper);

        try
        {
            ActSchema.Apply(database, mapper, log ?? NullLogger.Instance);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }
}
