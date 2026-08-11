1. smoke-test the Linux AppImage on a real Linux desktop
2. review app strings, like settings explaining too much
3. clean git commit history, delete old releases, tags, and PRs
4. make repo public and configure permissions
5. verify auto-update end to end — `electron-updater` reads releases anonymously, so every check
     404s while the repo is private and **the feature has never actually run**. On flip: cut two
     stable releases, install the first, confirm the second is found/downloaded/applied on exit;
     then cut a pre-release and confirm it is *not* offered. v0.0.1 shipped under the old `act-app`
     appId and must be uninstalled by hand once; nothing after it is affected.
6. promote it
