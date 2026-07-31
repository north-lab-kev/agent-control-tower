using Act.Core.Model;

namespace Act.Core.Abstractions;

// ⚠️ PENDING — a workaround for a CLI defect, not the design. For Claude Code the transcript path
// arrives in every hook payload and ACT is simply *told* where it is. Codex fires no hooks on the
// pinned CLI (see `docs/codex-hooks-findings.md`), so ACT has to infer the file by convention:
// scan the sessions folder and match a rollout against the launch it just made.
//
// **Delete this the moment Codex hooks fire.** `SessionStart` carries `transcript_path` and
// `session_id`, so the whole of this port and its implementation collapses into the one line the
// hook normalizer already has for Claude Code — `HookNormalization.TranscriptPath` — and the
// guesswork below (which file, whose launch, whose cwd) stops existing.
public interface ITranscriptFinder
{
    AgentType Agent { get; }

    // Null until the file exists, which for a fresh launch is normally the first second or two.
    TranscriptLocation? Locate(TranscriptSearch search);
}

// Two different searches, and conflating them is what let a stale rollout bind to a fresh card:
//
//   * **`SessionId` known** — a restored session, or one already bound. Then the id *is* the match
//     and nothing else matters: the file is whichever rollout carries it, however old.
//   * **`SessionId` null** — a fresh Codex launch, which is the only case with any guessing in it.
//     A candidate must be in the same directory, must have started no earlier than this **session**
//     did, and must not already be claimed by another live card.
//
// `StartedAfter` is when *this session* spawned, deliberately not the card's `LaunchedAt`: that field
// is stamped on a card's first launch and never again, so on a relaunch it is arbitrarily stale and
// every rollout made in between looks like a candidate. It cost a real mis-binding to learn.
public sealed record TranscriptSearch(
    string WorkingDir,
    DateTimeOffset StartedAfter,
    IReadOnlySet<string> Claimed,
    string? SessionId = null);

// `SessionId` is null when the file names no session — the path is still worth having, since
// enrichment does not need the id.
public sealed record TranscriptLocation(string Path, string? SessionId);
