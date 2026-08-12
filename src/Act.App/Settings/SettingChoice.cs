namespace Act.App.Settings;

public sealed record SettingChoice<TValue>(TValue Value, string Text);
