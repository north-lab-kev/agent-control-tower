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

// `Claimed` is what keeps two cards launched in the same directory from binding to each other's
// session: a path another live card already holds is not a candidate, however well it matches.
public sealed record TranscriptSearch(
    string WorkingDir,
    DateTimeOffset LaunchedAt,
    IReadOnlySet<string> Claimed);

// `SessionId` is null when the file names no session — the path is still worth having, since
// enrichment does not need the id.
public sealed record TranscriptLocation(string Path, string? SessionId);
