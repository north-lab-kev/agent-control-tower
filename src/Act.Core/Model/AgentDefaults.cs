namespace Act.Core.Model;

// Where an agent's CLI lives on this machine, and how it is invoked here every time. These are
// properties of the **installation**, not of a task: the path to `codex.exe` does not change
// because the work does, and a proxy variable that one task needs, every task on this machine
// needs. They were per-task fields on the form until 2026-08-01, which meant re-entering the same
// path on every card and no way to fix it in one place when it moved.
//
// One record per agent rather than one shared set: the two CLIs are installed separately, take
// different flags, and only one of them is ever the odd one out.
public sealed class AgentDefaults
{
    public AgentType Agent { get; set; }

    // Whether the task form offers this agent at all. Defaults to on, and startup discovery turns
    // it off **once** for an agent it cannot find anywhere — an agent that is not installed can
    // only produce cards that fail at spawn. After that first pass the switch is the user's alone,
    // so re-enabling one sticks.
    public bool Enabled { get; set; } = true;

    // Empty means "find it on `PATH`", which is the normal case. It is set when the install is not
    // there — a Store-packaged Codex lives under `%LOCALAPPDATA%\OpenAI\Codex\bin\…` and is on no
    // `PATH` at all.
    public string? Binary { get; set; }

    public IList<string> ExtraFlags { get; set; } = [];

    public IDictionary<string, string> Env { get; set; } = new Dictionary<string, string>();

    public AgentDefaults Copy() => new()
    {
        Agent = Agent,
        Enabled = Enabled,
        Binary = Binary,
        ExtraFlags = [.. ExtraFlags],
        Env = new Dictionary<string, string>(Env),
    };
}
