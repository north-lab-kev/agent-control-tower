using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Settings;

public sealed class UserSettingsService(ISettingsStore store, AppCulture culture, ISleepInhibitor sleep)
{
    private readonly Lock gate = new();

    private readonly UserSettings current = store.Load();

    public event Action? Changed;

    public LanguagePreference Language => current.Language;

    public ThemePreference Theme => current.Theme;

    public BoardDensity Density => current.Density;

    public bool KeepAwake => current.KeepAwake;

    public void ApplyLanguage() => culture.Apply(current.Language);

    // Called at startup as well as on every change, because a setting that only takes effect when
    // you toggle it is a setting that quietly turns itself off every time the app restarts.
    public void ApplyKeepAwake()
    {
        if (current.KeepAwake)
            sleep.Hold();
        else
            sleep.Release();
    }

    public void SetLanguage(LanguagePreference language)
    {
        if (language == current.Language)
            return;

        Update(settings => settings.Language = language);
    }

    public void SetTheme(ThemePreference theme)
    {
        if (theme == current.Theme)
            return;

        Update(settings => settings.Theme = theme);
    }

    public void SetDensity(BoardDensity density)
    {
        if (density == current.Density)
            return;

        Update(settings => settings.Density = density);
    }

    public void SetKeepAwake(bool keepAwake)
    {
        if (keepAwake == current.KeepAwake)
            return;

        Update(settings => settings.KeepAwake = keepAwake);
    }

    private void Update(Action<UserSettings> change)
    {
        lock (gate)
        {
            change(current);
            store.Save(current);
        }

        ApplyLanguage();
        ApplyKeepAwake();
        Changed?.Invoke();
    }
}
