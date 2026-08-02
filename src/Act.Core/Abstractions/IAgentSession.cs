using Act.Core.Events;

namespace Act.Core.Abstractions;

// One live agent session: its terminal, and the observations ACT makes about it. There is
// deliberately no way to answer a permission request or a question from here — ACT hosts
// the agent's real TUI and the user answers there, so the only input ACT itself supplies is
// what `IAgentTerminal.SubmitAsync` carries. Disposal ends the session, and the process
// stays alive for as long as the card is active rather than only for a turn, so a disposed
// session must complete its event stream rather than merely stop yielding.
public interface IAgentSession : IAsyncDisposable
{
    // ACT's own id, always known, and the only correlation handle that exists from the first
    // instant of a session — which matters because `SessionId` sometimes does not.
    Guid TaskId { get; }

    // Null until the agent's id is known. Claude Code takes a pre-minted `--session-id`, so it
    // is set before the process starts; Codex mints its own and reports it in `SessionStart`,
    // so it arrives a moment later. Nothing may assume it is populated at launch.
    string? SessionId { get; }

    IAgentTerminal Terminal { get; }

    IAsyncEnumerable<AgentEvent> Events { get; }
}
