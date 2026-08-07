using System.Text.Json;
using Act.Core.Model;

namespace Act.Core.Abstractions;

// One agent's hook dialect turned into ACT's vocabulary. It lives with the adapter for the same
// reason the command line does — the payload shape is a fact about that CLI — and it is pure
// enough to unit-test with a string of json and no process anywhere.
public interface IHookNormalizer
{
    AgentType Agent { get; }

    HookNormalization Normalize(JsonElement payload, string? knownSessionId, DateTimeOffset at);
}
