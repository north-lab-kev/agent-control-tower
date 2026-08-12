# Running the Linux AppImage on Windows (WSL2 + WSLg)

The app opens as a normal Windows window — taskbar button, alt-tab, Win+Shift+S screenshots.

1. Install WSL and an Ubuntu distro, from PowerShell:

   ```bash
   wsl --update
   ```

   ```bash
   wsl --install -d Ubuntu
   ```

2. Open the **Ubuntu terminal** and run everything below there, not in PowerShell. A normal
   Ubuntu shell is a login shell, which is what sets `DISPLAY=:0` — without it there is no
   window.

3. Install the runtime dependencies. If apt says *Unable to locate package*, drop the `t64`
   suffix from that name and retry.

   ```bash
   sudo apt update && sudo apt install -y libfuse2t64 libgtk-3-0t64 libnss3 libasound2t64
   ```

4. Copy the AppImage onto the Linux filesystem and make it executable. Do not run it straight
   from `/mnt/c` — `chmod +x` does not stick on the Windows mount and the AppImage cannot
   self-mount there.

   ```bash
   mkdir -p ~/act && cp /mnt/c/Users/<you>/Downloads/ACT-*-x86_64.AppImage ~/act/ && chmod +x ~/act/ACT-*.AppImage
   ```

5. Run it. `--no-sandbox` is already baked into the package, so nothing extra is needed.

   ```bash
   ~/act/ACT-*-x86_64.AppImage
   ```

6. The logs are at `~/.local/share/ACT/logs/act-<date>.log`, and the first line names the data
   directory ACT resolved. A missing `logs` directory is itself the finding: the .NET backend
   never reached the line that creates it.

   ```bash
   tail -50 ~/.local/share/ACT/logs/act-*.log
   ```

7. If the window is black or empty, Chromium is tripping over WSLg's GPU passthrough. Re-run
   with `--disable-gpu`.

8. If it sits on the splash screen forever, the backend died before it could log anything. Run
   the bundled binary directly to see the error Electron swallowed — the mount path is printed
   by `ls -d /tmp/.mount_ACT*` while the app is running, and the throwaway data directory and
   spare port keep the run from touching anything real. On a build from before ACT bundled its
   own ICU, the error names libicu and `sudo apt install -y libicu78` is the way past it:

   ```bash
   /tmp/.mount_ACT*/resources/bin/Act.App --ACT_DATA_DIR=/tmp/act-sandbox --Urls=http://localhost:5291
   ```

9. If it fails naming a missing `.so`, list every missing library at once and install what it
   names:

   ```bash
   ldd /tmp/.mount_ACT*/resources/bin/Act.App | grep "not found"
   ```

10. To reclaim the disk space afterwards, from PowerShell. This deletes the distro and its
    VHDX; other distros are unaffected.

    ```bash
    wsl --unregister Ubuntu
    ```

## What to expect

- **Separate store.** `ActDataDirectory` resolves inside the Linux filesystem, so this is a
  fresh empty board. Your Windows board is untouched.
- **No usage numbers, no auto-titles.** Those shell out to the Claude/Codex CLIs and read
  their credential files; neither is installed or authenticated in the distro.
