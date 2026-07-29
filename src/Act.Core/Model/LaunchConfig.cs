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
}
