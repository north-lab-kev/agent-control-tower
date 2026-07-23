# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Version control (HARD RULE)

- **Never `git add`/stage, `git commit`, or `git push` yourself unless I
  specifically ask for it in that message.** Make the file changes and stop.
  A prior request to commit/push does not carry over — each one requires its
  own explicit ask. "Fix it" / "address this" / reporting a problem is **not**
  permission to commit or push.

## Coding conventions

- **Do not write code comments.** Only add a comment when either:
  1. I explicitly ask for it, or
  2. the code is not final — a placeholder, stub, or otherwise pending final
     implementation. In that case the comment must flag the pending state.

  Finished code ships without comments; let clear names and small functions
  carry the meaning.

<!--
More build-time guidance to add:
- architecture guardrails (dependency direction, ports-and-adapters)
- domain vocabulary to use verbatim (cards, columns, badges, transitions, adapters, sources)
- commands and workflow rules

See docs/ACT-overview.md for the spec and docs/ACT-roadmap.md for the build sequence.
-->
