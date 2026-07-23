# Act.Desktop

> **Status: currently unused (pending removal decision).**
>
> Under the chosen Electron wiring (Option B), the Electron desktop shell lives
> **inside `Act.App`** via ElectronNET.Core — `Act.App` is both the web app and
> the desktop host. Packaging config (`electron-builder.json`) also lives in
> `Act.App/Properties/`.
>
> That leaves this project with no responsibility today. It's kept only as a
> possible future home for a genuinely shell-specific concern that shouldn't sit
> in `Act.App`. If none emerges, remove it.

**Build caveats (apply to the packaging in `Act.App`):** Node.js on the build
machine; Linux builds from Windows need WSL2; producing the packaged artifact on
Windows needs symlink privilege (Developer Mode or an elevated shell) for
electron-builder's code-sign toolchain.
