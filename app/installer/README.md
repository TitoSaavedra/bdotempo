# Installer, portable zip and releases

The release artifacts are built into `dist/` at the repository root (git-ignored):

| File | What it is |
|------|------------|
| `BdoComboSetup-Setup-<version>.exe` | Installer (Inno Setup 6): the app, the 17 asset parts, a Start Menu shortcut and an uninstaller. |
| `BdoComboSetup-Portable-<version>.zip` | Portable: the app, `assets.bdoa` already assembled and `portable.flag`. No installer. |
| `SHA256SUMS.txt` | `sha256sum` format, for the artifacts of this version. The in-app updater verifies the installer with it. |

The version is the single source of truth: `<Version>` in `app/Directory.Build.props` (SemVer 2.0). It is shown in
Settings and read by the updater.

## Commands

```
mise run package-installer   # installer + SHA256SUMS.txt
mise run package-portable    # portable zip + SHA256SUMS.txt
mise run package             # both
```

## One manual step: Inno Setup 6

mise has no backend for Inno Setup (no aqua, ubi or github package), so it cannot install it. Install it once:

```
winget install --id JRSoftware.InnoSetup -e
```

or download it from https://jrsoftware.org/isdl.php. The packaging script finds `ISCC.exe` in `PATH`, in
`Program Files (x86)\Inno Setup 6`, in `Program Files\Inno Setup 6`, in `%LOCALAPPDATA%\Programs\Inno Setup 6`, or in
the path of the environment variable `INNO_SETUP_COMPILER`. If none is found, the task stops with that message.

## The app needs the .NET 10 Desktop Runtime (x64)

The app is framework-dependent (`SelfContained=false`): the PC needs the .NET 10 Desktop Runtime. The Windows App SDK
runtime is bundled in the app folder (`WindowsAppSDKSelfContained=true`), so it is not needed on the PC.

- The installer checks for the runtime at start: a `10.x` folder under `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App`
  and under `...\Microsoft.NETCore.App`. The registry is not used: a runtime installed by mise, or by an archive,
  has no registry entry.
- If it is missing, the installer asks: **Yes** runs `winget install Microsoft.DotNet.DesktopRuntime.10` (Windows asks for
  permission), **No** opens https://dotnet.microsoft.com/download/dotnet/10.0, **Cancel** stops. A silent installation
  (the updater) does not ask: it refuses and writes the reason to its log.
- The portable zip has no check: run it on a PC with the runtime.

## What the installer does

1. Copies the app to `{autopf}\BDO Combo Setup` (per user by default; an administrator can choose the whole PC).
2. Copies `assets\parts\assets.part*.bdoa` into `{app}\assets\parts`.
3. Runs `BdoComboSetup.exe --assemble-assets "{app}\assets\parts" "{app}\assets.bdoa"` and then deletes the parts folder.
   If the command fails, the installer ends with exit code 1, shows the error (interactive run) and keeps the parts.
4. The uninstaller removes `assets.bdoa` and the `assets` folder. The logs in `%LOCALAPPDATA%\BdoComboSetup\logs` stay.

An update over an installed copy closes the running app (`CloseApplications`). The installer `AppId` is fixed; do not
change it.

Silent install and uninstall (used by the updater and for tests):

```
BdoComboSetup-Setup-<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="<folder>" /LOG="<log file>"
"<folder>\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="<log file>"
```

## WinUI packaging notes

- The app is unpackaged (`WindowsPackageType=None`). The framework-dependent publish is a folder of files, not a
  single-file exe: the single-file build depended on the self-contained runtime, and the installer embeds the folder anyway.
- `Program.cs` still sets `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` to the app folder before the first WinRT call. It is
  harmless in a folder publish and keeps the single-file case working.
- The exe reads `assets.bdoa` from its own folder (`AppContext.BaseDirectory`). A publish folder has no `assets.bdoa`:
  the packaging assembles it (portable) or the installer does (installed copy).
