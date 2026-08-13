using LiteDB;

namespace Act.Infrastructure.Storage;

// Asked **before** the container opens the store, which is the whole point. Opening it is what applies
// the migrations, and a store written by a newer build has nothing to migrate *to* — `ActSchema` throws,
// and that throw arrives on the startup thread long before Electron's bridge exists, so there is no way
// to say why on screen. A downgraded ACT then dies behind its own splash screen, which is the same
// failure the update pump's `Configure` once had.
//
// So the question is asked with no container, no mapper contract and no migration: open, read one
// integer, close. What the caller does with an unsupported answer is a shell decision — see `Program`.
public static class ActStoreCompatibility
{
    // A store that does not exist yet cannot be from the future. Checked rather than opened, because
    // `new LiteDatabase` **creates** the file, and a probe that leaves an empty `act.db` behind would
    // hand the real open a store it has to migrate from nothing on every first run.
    public static StoreCompatibility Inspect(string dataDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(dataDirectory), ActDatabase.FileName);

        if (!File.Exists(path))
            return new StoreCompatibility(0, ActSchema.CurrentVersion);

        using var database = new LiteDatabase(path, ActBsonMapper.Create());

        return new StoreCompatibility(ActSchema.StoredVersion(database), ActSchema.CurrentVersion);
    }
}

// `Stored` is 0 for a store that does not exist and for one written before versioning; both are
// supported, because both migrate forward. Only a *higher* number is refused.
public readonly record struct StoreCompatibility(int Stored, int Understood)
{
    public bool IsSupported => Stored <= Understood;
}
