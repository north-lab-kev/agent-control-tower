1. smoke-test the Linux AppImage on a real Linux desktop
2. review app strings, like settings explaining too much
3. clean git commit history, delete old releases, tags, and PRs
4. make repo public and configure permissions
5. verify auto-update end to end — `electron-updater` reads releases anonymously, so every check
     404s while the repo is private and **the feature has never actually run**. On flip: cut two
     stable releases, install the first, confirm the second is found/downloaded/applied on exit;
     then cut a pre-release and confirm it is *not* offered. v0.0.1 shipped under the old `act-app`
     appId and must be uninstalled by hand once; nothing after it is affected.
6. decide where the nested-session env block belongs: the global Claude Code agent defaults carry
   `CLAUDECODE=0`, `CLAUDE_CODE_ENTRYPOINT=cli` and four empty `CLAUDE_*` variables (lifted from a
   card that worked around ACT being launched from inside a Claude Code session) — should that be
   ACT's own spawn code instead of a user setting?
7. decide what an `e2e` CI failure means long-term — it blocks every push today; if it starts
   flaking, move it to `schedule` + `workflow_dispatch` rather than `continue-on-error`
