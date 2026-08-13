# Native OS notifications

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

Load-bearing, not cosmetic: when you're not watching the board (especially
overnight), the OS notification **is** the "needs you" signal. Without it,
unattended mode is half-blind.

## Events → triggers (map to attention-state transitions)

- **Blocked in Your turn** — `needs permission`, `needs answer`, `error`,
  `killed` (blocked on you). Always on.
- **`to review` in Your turn** — a task finished and wants sign-off. This *is* the
  overnight "it's done" ping, since nothing completes itself.
- **Usage limit reached / rate-limited** — queue held until the window resets, and
  again when it resumes.

There is nothing to notify for a *quiet* card: silence is not a state and ACT makes
no claim about it (see *No stale badge*). Waking someone for a card that may simply
be running a long build is exactly the false alarm the chip exists to avoid.

## Behavior rules

- **Actionable.** Clicking the notification reveals the window and navigates
  **into that card's terminal view** — the place the user has to be to answer
  anyway. The whole toast is the target rather than a button on it: Windows gives
  an unpackaged app the body click and nothing more, and one destination needs no
  second control. ACT carries no approve/deny buttons, in the notification or
  anywhere else.
- **Respect focus.** Suppress the popup when ACT is focused **and on the board**
  (the in-app pulse covers it); notify mainly when ACT is backgrounded — the
  overnight case. Focused on a *card* still notifies: the board is the only
  surface that shows every card's state at once.
  - A state suppressed because the user was watching is **not** replayed when the
    window later loses focus. The notification is about the moment the card
    changed, and that moment was seen.
- **One state, one ping — and no digest.** The same state never fires twice for
  the same task (several sources report the same block, and a card is written
  again for reasons that are not changes). Bursts are **deliberately not
  coalesced**: a digest saying *"4 need you"* cannot deep-link
  to any of the four, which throws away the one thing that makes the notification
  actionable. Five cards finishing at once is five toasts, and that is the right
  trade.
- **One switch, not a matrix.** Notifications are on or off (*Settings → System*,
  default on, desktop only). There is deliberately no per-kind matrix: nobody
  wants to hear that a task failed but not that one finished, and every extra
  toggle is another way to silence the signal unattended mode depends on.

## App identity on the toast (Windows)

Windows takes the header line and the icon from the **Application User Model ID**,
not from anything in the notification. Left alone, every ACT toast announces itself
as `electron.app.Electron`.

- ACT sets its own id (`com.northlabkev.act`, mirroring the GitHub org) at startup,
  and the installer's `appId`
  is the **same string** — the NSIS shortcut is what maps the id to the product
  name, so a mismatch there would leave the packaged build no better off.
  - **They did not match until auto-update forced the issue.** ElectronNET's targets pass
    `-c.appId "$(ElectronPackageId)"` on the electron-builder command line, and a CLI `-c` overrides
    the value in `electron-builder.json`; unset, `ElectronPackageId` defaults to the project name,
    so v0.0.1 shipped as `act-app` while the running app claimed `com.northlabkev.act`. It is now
    set explicitly in `Act.App.csproj`. **This is a one-way door:** NSIS derives its uninstall key
    from the appId, so changing it again after auto-update ships would install a second copy beside
    the first rather than upgrade it. v0.0.1 has to be uninstalled by hand once; nothing after it
    does.
- **Unpackaged runs still show the raw id**, because a `dotnet run` has no
  Start-menu shortcut to resolve a name from. Accepted rather than fixed: the
  alternative is ACT writing a display-name key into the user's registry on every
  machine it runs on, which is an installer's job, not a dev session's.
- The icon rides the notification itself (`icon.ico`, the same file the window and
  tray use) and therefore works in both modes.

## Updates — how a new version reaches an installed copy

`electron-updater` (already a runtime dependency of the Electron host) polls the repository's GitHub
releases; the user-facing half is the *Check for updates* switch above. The mechanics are all in
what gets **published**, and two of them are counter-intuitive enough to write down.

- **A `publish` block in `electron-builder.json` is what makes any of it possible.** It is what makes
  electron-builder write `latest.yml` beside the installer and embed `app-update.yml` into the
  package — and the embedded file is the *only* way the feed reaches the updater, because
  Electron.NET's bridge exposes `getFeedURL` and no setter. Nothing is uploaded by electron-builder:
  update files are written whatever the publish policy is (`PublishManager.artifactCreated`), and the
  release workflow attaches them with `gh`.
- **The provider is `generic`, pointed at GitHub's own asset route — and that is load-bearing.**
  The obvious `github` provider **cannot package without a `GH_TOKEN`**. Electron.NET's targets never
  pass `-p` to electron-builder, so `PublishManager` picks the policy itself and lands on
  `onTagOrDraft`; its `isCi` is `require("ci-info")` — the module *object*, always truthy — so this
  happens on a developer's machine as readily as on CI. It then constructs a publisher before
  deciding it has nothing to upload, and `GitHubPublisher`'s constructor throws on a missing token.
  `createPublisher` short-circuits `generic` to `null` ahead of all that, so packaging needs no
  credential and makes no network call, while `latest.yml` and `app-update.yml` are still written —
  `PublishManager` says so in as many words: *"file should be generated regardless of publish
  state"*. `releases/latest/download/` resolves to the newest stable release and serves assets by
  name, which is where the `github` provider would have arrived anyway, and `GenericProvider`
  resolves the `.exe` and `.blockmap` from that same base. **ACT holds no publishing token: the only
  token in the pipeline is the ephemeral `secrets.GITHUB_TOKEN` the release job hands to `gh`, which
  cannot be avoided because creating a release requires one.**
- **Pre-releases are excluded twice over, and both are needed.**
  1. **`AllowPrerelease = false`, forced on every run.** `AppUpdater`'s constructor runs
     `allowPrerelease = hasPrereleaseComponents(currentVersion)`, quietly overriding its own `false`
     default — so a `0.1.0-beta.1` install tracks betas unless told otherwise. `AllowDowngrade`
     stays off beside it, so a beta is never walked back to an older stable.
  2. **A pre-release release carries no update metadata.** electron-builder writes `latest.yml` for
     *every* version, so the exclusion is done in the release job, which attaches the `.yml` and the
     `.blockmap` only when the version is stable. The `releases/latest/download` route already skips
     pre-releases; this makes it moot if it ever stops.

     **That "every version" is bought by `detectUpdateChannel: false`, and it is not the default.**
     Left on, electron-builder reads a channel out of the version's pre-release tag —
     `appInfo.channel` returns the first component, so `0.0.1-alpha.1` becomes `alpha` — and names
     the file after it (`updateInfoBuilder`: `publishConfig.channel || "latest"`). The build then
     emits `alpha.yml` and **no `latest.yml` at all**, which the release job's assertion catches as
     "auto-update would be dead on arrival". The `github` provider silently ignored the derived
     channel (`getResolvedPublishConfig` only applies it via `checkAndResolveOptions`, which exists
     on the S3 classes alone), so this trap was invisible until the move to `generic`, which applies
     it directly. One channel is what ACT wants: betas are excluded by withholding metadata, not by
     giving testers a channel of their own.

  The result for a beta tester: they sit still until a stable version semver-greater than their
  build appears, and then roll forward to it. **Betas are a dead end by design.**
- **Linux ships as an AppImage because of this machinery.** `AppImageUpdater` is the one Linux
  updater in electron-updater that works against this feed — a `.deb` or `.rpm` would ship with
  auto-update dead on arrival. It reads `latest-linux.yml` from the same generic base, and the
  release job attaches that file under the same stable-only rule as `latest.yml`.
- **Every release asset names its OS, and the feed is untouched by that.** The release page is one
  flat list of both platforms' files, so the installers carry the OS in the name electron-builder
  renders (`ACT-Setup-<version>-win-x64.exe`, `ACT-<version>-linux-x86_64.AppImage`) and the release
  job attaches every asset under a GitHub *label* that starts with `Windows` or `Linux`. The label
  is what the page shows and the **filename** is what `releases/latest/download/<name>` resolves, so
  labelling costs the updater nothing — and it is the only way to distinguish `latest.yml` from
  `latest-linux.yml`, whose names electron-updater fixes. Renaming an installer is equally safe:
  electron-builder writes the name it produced into the `.yml`'s `path`. `ReleaseAssetNamingTests`
  pins the templates against what the release job looks for, since nothing compiles against either.
- **Unsigned is fine, for now.** `NsisUpdater.verifySignature` returns null and skips when
  `app-update.yml` carries no `publisherName`; HTTPS to GitHub plus the sha512 in `latest.yml`
  covers integrity. Signing is worth doing, separately.
- **Two bridge hazards are worked around in `ElectronUpdater`, not endured.** The bridge's
  `downloadUpdate` handler awaits with no `.catch`, so a failed download never emits its completion
  and the awaiting task would hang for the life of the process — every call is bounded by a timeout.
  And `UpdateCheckResult` drops `isUpdateAvailable` crossing the bridge, so availability is read
  from the `update-available` / `update-not-available` events instead, with the completed check used
  only to bound the wait.
- **Development is quiet by construction.** `isUpdaterActive()` is false for an unpacked build, so a
  `dotnet run` resolves its check with nothing and reports *could not reach the feed*.

## Shell caveat (build note)

Full native notifications are effectively a **desktop-shell (Electron)
capability**. Under a plain `dotnet run` browser tab you'd be limited to Web
Notifications (permission grant required, unreliable when backgrounded) — another
point for the Electron packaging.
