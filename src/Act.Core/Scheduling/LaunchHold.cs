using Act.Core.Model;

namespace Act.Core.Scheduling;

// Why a Ready card is not running. Ordered most-durable first, which is also the precedence the
// board renders by: only one chip fits, and the reason that will still be true in an hour is worth
// more than the one that clears when the next card is signed off.
public enum LaunchHold
{
    Paused,
    AgentDisabled,
    UsageLimit,
    WorkingDir,
    Dependency,
    Slot,
}

// The hold with what the chip needs to say it. `Blocker` is the card in the way (folder or
// dependency), `Until` the instant a usage window resets, `Used`/`Cap` the concurrency figures —
// each null or zero for the holds they say nothing about.
public sealed record ReadyHold(
    LaunchHold Reason,
    Card? Blocker = null,
    DateTimeOffset? Until = null,
    int Used = 0,
    int Cap = 0);
