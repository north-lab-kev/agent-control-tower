using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface ISettingsStore
{
    UserSettings Load();

    void Save(UserSettings settings);
}
