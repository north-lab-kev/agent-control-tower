using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Settings;

public sealed class UserSettingsService(ISettingsStore store)
{
    private readonly Lock gate = new();

    private readonly UserSettings current = store.Load();

    public event Action? Changed;

    public ThemePreference Theme => current.Theme;

    public BoardDensity Density => current.Density;

    public void SetTheme(ThemePreference theme)
    {
        if (theme == current.Theme)
        {
            return;
        }

        Update(settings => settings.Theme = theme);
    }

    public void SetDensity(BoardDensity density)
    {
        if (density == current.Density)
        {
            return;
        }

        Update(settings => settings.Density = density);
    }

    private void Update(Action<UserSettings> change)
    {
        lock (gate)
        {
            change(current);
            store.Save(current);
        }

        Changed?.Invoke();
    }
}
