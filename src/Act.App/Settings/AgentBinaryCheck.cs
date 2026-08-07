using Act.Core.Abstractions;

namespace Act.App.Settings;

// What the settings page says about an agent's executable box, answered by the **same rules the
// launch uses** — a value with no separator is a name to resolve on `PATH`, anything else is a path
// that has to exist. Checking it any other way would let the box look fine and the launch still
// fail with "not found", which is the one thing this exists to prevent.
public enum AgentBinaryStatus
{
    // Empty, and the bare name really does resolve. The normal install, and nothing to say.
    OnPath,

    // A path or name that resolves. Worth confirming, because the user typed it for a reason.
    Found,

    // Typed, and nothing is there.
    NotFound,

    // Empty, but the CLI is installed somewhere that is **not** on `PATH`. The box looks harmless
    // and the launch will still fail, because an empty setting means "resolve the bare name" and
    // there is nothing of that name to resolve. The one state that needs the path in its message,
    // since the fix is to put it in the box.
    NotOnPath,

    // Empty, and nothing anywhere ACT knows to look. Not an error — a machine with one agent is a
    // normal machine — but it is why this agent's launches will fail.
    NotInstalled,
}

public sealed record AgentBinaryState(AgentBinaryStatus Status, string? DiscoveredPath = null);

public static class AgentBinaryCheck
{
    // The empty case is delegated to the adapter rather than re-deriving a binary name here: it
    // already knows what its CLI is called and where it installs, and asking it keeps that
    // knowledge in one place.
    public static AgentBinaryState For(IExecutableProbe probe, IAgentAdapter adapter, string binary)
    {
        if (string.IsNullOrWhiteSpace(binary))
            return adapter.Locate(probe) switch
            {
                { ExplicitPath: { } found } => new AgentBinaryState(AgentBinaryStatus.NotOnPath, found),
                { Found: true } => new AgentBinaryState(AgentBinaryStatus.OnPath),
                _ => new AgentBinaryState(AgentBinaryStatus.NotInstalled),
            };

        var value = binary.Trim();

        if (!Path.IsPathRooted(value)
            && !value.Contains(Path.DirectorySeparatorChar)
            && !value.Contains(Path.AltDirectorySeparatorChar))
            return new AgentBinaryState(probe.OnPath(value) is null
                ? AgentBinaryStatus.NotFound
                : AgentBinaryStatus.Found);

        return new AgentBinaryState(probe.FirstExisting([value]) is null
            ? AgentBinaryStatus.NotFound
            : AgentBinaryStatus.Found);
    }
}
