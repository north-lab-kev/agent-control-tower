namespace Act.Core.Model;

public sealed class LaunchConfig
{
    public string? AgentBinary { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public PermissionMode PermissionMode { get; set; }

    public IList<string> AllowedTools { get; set; } = [];

    public IList<string> DisallowedTools { get; set; } = [];

    public IList<string> ExtraFlags { get; set; } = [];

    public IDictionary<string, string> Env { get; set; } = new Dictionary<string, string>();

    // Every adapter resolves a requested config into the one it will actually launch with,
    // and none of them may mutate what the card is still carrying.
    public LaunchConfig Copy() => new()
    {
        AgentBinary = AgentBinary,
        Model = Model,
        Effort = Effort,
        PermissionMode = PermissionMode,
        AllowedTools = [.. AllowedTools],
        DisallowedTools = [.. DisallowedTools],
        ExtraFlags = [.. ExtraFlags],
        Env = new Dictionary<string, string>(Env),
    };
}
