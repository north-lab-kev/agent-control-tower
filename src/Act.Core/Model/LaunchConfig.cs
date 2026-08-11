namespace Act.Core.Model;

// What one launch is made of. The card owns `Model`, `Effort` and `PermissionMode`; the other three
// come from this machine's `AgentDefaults` and are filled in by `LaunchComposition` on the way to
// the adapter, so a card never stores them.
//
// There is deliberately no allowed/disallowed tool list: Codex has no equivalent and would drop it
// silently — the one outcome `LaunchConfigResolution` exists to make impossible. The permission
// mode is the knob that works on both agents.
public sealed class LaunchConfig
{
    public string? AgentBinary { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public PermissionMode PermissionMode { get; set; }

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
        ExtraFlags = [.. ExtraFlags],
        Env = new Dictionary<string, string>(Env),
    };
}
