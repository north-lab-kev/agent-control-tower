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

    // Gaps only, for the one-time lift of what used to be typed per card: a value already here is
    // the user's and outranks anything a card remembers, so this can never undo a deliberate
    // change — which is what makes it safe to run against a half-populated settings document.
    public AgentDefaults FillGapsFrom(AgentDefaults other)
    {
        if (string.IsNullOrWhiteSpace(Binary))
            Binary = other.Binary;

        if (ExtraFlags.Count == 0)
            ExtraFlags = [.. other.ExtraFlags];

        if (Env.Count == 0)
            Env = new Dictionary<string, string>(other.Env);

        return this;
    }

    public AgentDefaults Copy() => new()
    {
        Agent = Agent,
        Enabled = Enabled,
        Binary = Binary,
        ExtraFlags = [.. ExtraFlags],
        Env = new Dictionary<string, string>(Env),
    };
}
