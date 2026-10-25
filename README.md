# HyperDL Windows installer

`HyperDL.iss` defines a per-user x64 installer. It installs the self-contained
application under `%LOCALAPPDATA%\Programs\HyperDL` without requiring
administrator privileges. Inno Setup manages the uninstall registration and
the actual uninstaller; the Start Menu uninstall shortcut uses the
`{uninstallexe}` constant and does not rename the generated executable.

The installer does not define an uninstall-delete section. Normal removal
keeps user downloads, `.part` files outside the installation directory, and
`%LOCALAPPDATA%\HyperDL` settings and download history.

## Requirements

- Windows x64.
- .NET 8 SDK at `C:\Program Files\dotnet\dotnet.exe`.
- Inno Setup 6.

## Build

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script restores, builds, tests, and publishes HyperDL before locating
Inno Setup and compiling the installer. The output is:

```text
installer\Output\HyperDL_Setup.exe
```

The script reports the file size and SHA-256 checksum. To run the static
installer checks without installing the application:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-installer.ps1
```

## Manual installation and uninstall checks

1. Install on a test account and confirm the default per-user location.
2. Confirm the Start Menu application and `Uninstall HyperDL` shortcuts.
3. Confirm Windows Installed Apps lists HyperDL.
4. Launch the application and verify the About/file version and publisher.
5. Uninstall from Windows Settings and separately test the Start Menu shortcut.
6. Confirm application files and shortcuts are removed while user downloads,
   partial downloads, settings, and download history remain.
7. Reinstall and test an upgrade over an existing installation.

Inno Setup chooses the generated uninstaller filename, commonly
`unins000.exe`. Do not rename it: use the Inno Setup-managed `{uninstallexe}`
reference. Do not test destructive data removal against real user data.

Before publishing, manually verify clean install, upgrade, shortcuts, Windows
Installed Apps registration, download and recovery workflows, and both
uninstall entry points. The installer is not code-signed by this project.

Traditional Chinese: [HyperDL Windows 安裝程式](README.zh-TW.md).
