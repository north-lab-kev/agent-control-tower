using Act.Core.Model;

namespace Act.App.Settings;

// One agent's install, as text boxes. Settings apply on change rather than on a save button, so
// this exists to hold the half-typed state and to own the two conversions the stored shape needs:
// a flag list and an environment map are both edited as one-per-line text.
public sealed record AgentDefaultsForm
{
    public AgentType Agent { get; init; }

    public bool Enabled { get; set; } = true;

    public string Binary { get; set; } = string.Empty;

    public string ExtraFlags { get; set; } = string.Empty;

    public string Env { get; set; } = string.Empty;

    // Recomputed when the box changes rather than read during render: it touches the filesystem,
    // and a render path is the wrong place to do that on every frame.
    public AgentBinaryState State { get; set; } = new(AgentBinaryStatus.OnPath);

    public static AgentDefaultsForm From(AgentDefaults defaults) => new()
    {
        Agent = defaults.Agent,
        Enabled = defaults.Enabled,
        Binary = defaults.Binary ?? string.Empty,
        ExtraFlags = string.Join('\n', defaults.ExtraFlags),
        Env = string.Join('\n', defaults.Env.Select(variable => $"{variable.Key}={variable.Value}")),
    };

    public AgentDefaults ToDefaults() => new()
    {
        Agent = Agent,
        Enabled = Enabled,
        Binary = string.IsNullOrWhiteSpace(Binary) ? null : Binary.Trim(),
        ExtraFlags = Lines(ExtraFlags),
        Env = Variables(Env),
    };

    // Newlines and commas, not whitespace: a flag can carry a value with a space in it.
    private static IList<string> Lines(string value)
        => [.. value.Split(
            ['\n', '\r', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static IDictionary<string, string> Variables(string value)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in value.Split(
            ['\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            variables[line[..separator].TrimEnd()] = line[(separator + 1)..].TrimStart();
        }

        return variables;
    }
}
