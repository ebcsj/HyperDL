# HyperDL

> A Windows download manager for large files, designed for efficient transfers, reliable recovery, and lower memory use.

[English](README.md) | [繁體中文](README.zh-TW.md)

[User guide](docs/user-guides/USER_GUIDE.en.md) ·
[Troubleshooting](docs/user-guides/TROUBLESHOOTING.en.md) ·
[Project structure](docs/PROJECT_STRUCTURE.md) ·
[Documentation index](docs/README.md)

![Windows](https://img.shields.io/badge/Platform-Windows_x64-0078D4)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-6C2EB9)
![Windows x64](https://img.shields.io/badge/Target-win--x64-0078D4)
![MIT License](https://img.shields.io/badge/License-MIT-green)

## Overview

HyperDL is built with C#, .NET 8, and WPF. It focuses on large files available
through direct HTTP/HTTPS URLs. File types depend on the source and may include
videos, ISO images, archives, EXE and MSI files, and installers.

When a server confirms byte-range support and the file size is known, HyperDL
uses fixed ranges for concurrent downloads. If ranges are unsupported, the
size is unknown, or safe segmentation is not possible, it falls back to a
single connection. Data is streamed to disk using bounded buffers; the
complete file or a complete segment is not loaded into memory.

Segmented jobs use one `.part` file and SQLite job records. Low RAM mode
reduces the buffer size used by each worker. Actual memory use also depends on
the number of active workers, files, the operating system, and other running
software. More connections do not necessarily improve speed; server limits,
the ISP, Wi-Fi, the network adapter, latency, and disk performance all affect
results.

HyperDL is an independently developed open-source project created by
**Kerby Chow**. It is not an official version of Internet Download Manager,
Motrix, or any other download manager, and does not include their source code,
trademarks, or official assets.

## Project Status

> The latest HyperDL release has been uploaded. Download it or run the new Setup to update an existing installation.

- ✅ Complete: Dynamic segmentation (cooperative split) for eligible range-supported files ≥ 64 MiB (default on, configurable).
- ✅ Complete: Buffer modes (`Auto`, `Low RAM`/`LowRam`, `Balanced`, `Fast`), global 64 MiB budget, per-download overrides.
- ✅ Complete: Retry with backoff/jitter, idle-timeout protection, `If-Range` on resume, source-change detection.
- ✅ Complete: Disk space preflight and FAT32 4 GB limit checks with localized errors.
- ✅ Complete: Per-engine bandwidth limiter, per-host/global connection limits, concurrency gate.
- ✅ Complete: Pending queue persistence, auto-resume on startup, graceful shutdown checkpointing.
- ✅ Complete: Bilingual UI (English / 繁體中文) with runtime language switching, resource-based localization.
- ✅ Complete: Chrome extension bridge via `hyperdl://` protocol (HKCU registration), CLI/single-instance IPC.
- ✅ Complete: Multi-select, context menu, search/filter, clear completed, edit URL, delete files, open logs.

Statuses describe the current code and tests, not a promise to deliver planned features.

## ✨ Features

- Windows WPF download list, job status, and history.
- Direct HTTP/HTTPS URL validation and range probing.
- Validation of `206 Partial Content`, `Content-Range`, requested ranges, and response lengths.
- Fixed-range concurrent downloads when eligible; otherwise, single-connection streaming.
- Auto and 1, 2, 4, 8, 16, 32, or 64 connection choices, subject to a minimum segment size.
- `RandomAccess.WriteAsync` writes segments to their offsets in one `.part` file.
- Pause, Resume, and Cancel for eligible segmented jobs.
- A fresh source probe before Resume, comparing file size, ETag, Last-Modified, and range support.
- SQLite download and segment metadata, with recoverable jobs loaded at startup.
- Progress, speed, ETA, file information, and segment status display.
- Low RAM mode and a global active-connection limit.
- Filename sanitization, Windows reserved-name handling, and a safe fallback name.
- xUnit tests, win-x64 self-contained publishing, and an Inno Setup 6 installer.

## Development Status

| Feature | Status | Notes |
|---|---|---|
| HTTP/HTTPS | ✅ Complete | Direct HTTP or HTTPS file URLs only. |
| HTTP Range | ✅ Complete | Probe and download responses validate partial content and range information. |
| Single-connection fallback | ✅ Complete | Streams data when ranges are unsupported or size is unknown. |
| Auto mode | ✅ Complete | Chooses a target connection count by file size, then applies range and minimum-segment limits. |
| Fixed connection modes | ✅ Complete | Offers 1, 2, 4, 8, 16, 32, or 64. |
| Pause / Resume | ✅ Complete | For segmented jobs with known size and confirmed range support. |
| Cancel | ✅ Complete | Cancelling an active segmented job removes its incomplete `.part` file. |
| SQLite recovery | ✅ Complete | Saves download and segment state; eligible interrupted jobs are marked Recoverable. |
| Low RAM | ✅ Complete | Low RAM worker buffer is 64 KiB; standard mode uses 80 KiB. |
| Installer | ✅ Complete | Inno Setup definition and build script; Inno Setup 6 is required to compile. |
| FTP / FTPS | ❌ Not supported | No FTP/FTPS client is implemented. |
| BitTorrent | ❌ Not supported | Torrent and magnet downloads are unsupported. |
| Automatic updates | ❌ Not supported | No update feed, signature verification, or update installation flow exists. |
| Dynamic segmentation | ✅ Complete | Cooperative split for eligible files ≥ 64 MiB (configurable). |
| Buffer modes | ✅ Complete | `Auto` / `LowRam` / `Balanced` / `Fast` with 64 MiB global budget. |
| Browser integration | ✅ Complete | `hyperdl://` protocol + Chrome extension bridge. |
| Localization | ✅ Complete | English and 繁體中文 (runtime switchable). |

## Memory-efficient Design

Downloads are streamed:

```text
Network stream → bounded worker buffer → file on disk
```

HyperDL does not load a complete file or segment into RAM and does not use a
large `MemoryStream` to hold a download. Buffer sizes are determined by the
selected buffer mode (`Auto`, `LowRam`, `Balanced`, or `Fast`) and are limited
by a global 64 MiB network-buffer budget. Active worker counts and other system
activity also affect memory, so no fixed total-RAM limit is promised.

## Connection Modes

Available choices are Auto, 1, 2, 4, 8, 16, 32, and 64. Auto uses the known
file size to select a target:

| File size | Auto target |
|---|---:|
| Less than 8 MiB | 1 |
| 8 MiB to less than 32 MiB | 2 |
| 32 MiB to less than 128 MiB | 4 |
| 128 MiB to less than 1 GiB | 8 |
| 1 GiB to less than 8 GiB | 16 |
| 8 GiB to less than 32 GiB | 32 |
| 32 GiB or larger | 64 |

Auto values are targets, not guarantees. Range support must be confirmed, the
file size must be known, and each segment must meet the 4 MiB
`MinimumSegmentSize`; otherwise, HyperDL reduces the segment count or uses one
connection. Auto is a sensible everyday choice. More connections may cause
server throttling, HTTP 429 responses, or lower speeds.

## Crash Recovery

For segmented jobs, the original and final URLs, paths, file size, status,
source validators, and segment offsets are stored in
`%LOCALAPPDATA%\HyperDL\hyperdl.db`. Active jobs are checkpointed about every
five seconds and when their state changes.

```text
Downloading → .part + SQLite checkpoint → application restart
            → Recoverable → user selects Resume
```

Downloads do not restart automatically. Before resuming, HyperDL checks the
`.part` length, probes the source again, and compares file size, ETag,
Last-Modified, and range support. Resume is stopped if compatibility cannot be
confirmed. When a server provides no comparable validators, not every remote
content change can be detected.

SQLite stores checkpoint offsets; it is not synchronously updated for every
buffer. Data written after the last checkpoint may need to be downloaded
again after an unexpected termination.

## Download

Download the latest release from the project's GitHub Releases page. The
recommended file for most users is `HyperDL_Setup.exe`. To update an existing
installation, download and run the latest Setup.

1. Download `HyperDL_Setup.exe` from the project's GitHub Releases page.
2. Download `HyperDL-win-x64.zip` only if that Portable ZIP is explicitly attached to the release; the current publish script does not create a ZIP.
3. If the release provides a SHA-256 checksum file, calculate the local file hash in PowerShell and compare it to that trusted checksum:

   ```powershell
   Get-FileHash .\HyperDL_Setup.exe -Algorithm SHA256
   ```

4. Do not download the installer from an unknown third-party site. If no release checksum is provided, a locally calculated hash is not an independent verification source.

The installer may not be code-signed, so Windows SmartScreen may show a
warning. Check the official release page and a trusted SHA-256 value before
deciding whether to run the file; do not bypass warnings blindly.

## Installation

### Using the installer

1. Download `HyperDL_Setup.exe` from the official release.
2. Double-click the installer.
3. Review the setup information and choose an install folder. The default is `%LOCALAPPDATA%\Programs\HyperDL`.
4. Choose whether to create a desktop shortcut.
5. Select Install.
6. On the completion page, choose Launch HyperDL or start it from the Start Menu.

This is a per-user installation and normally does not require administrator
privileges. The Start Menu includes HyperDL and Uninstall HyperDL; Windows
Installed Apps also lists HyperDL.

### Portable / ZIP version

No Portable ZIP is currently built or distributed. `scripts/publish.ps1`
creates a self-contained publish directory but does not package it as a ZIP.
Follow separate instructions only if a future release explicitly includes
that archive.

## First Launch

1. Start HyperDL from the desktop shortcut, Start Menu, or installation folder.
2. Review the legal-use notice shown on the first launch.
3. Open Settings and confirm the download folder, connection count, maximum concurrent downloads, and Low RAM option.
4. The default download folder is `%USERPROFILE%\Downloads`; change it in Settings if needed.
5. Download history and settings are stored under `%LOCALAPPDATA%\HyperDL`.

## Usage

### Adding a download

1. Launch HyperDL.
2. Enter a direct HTTP or HTTPS file URL in the URL field.
3. Confirm the download folder in Settings. Open Download Folder opens the selected job's folder or the default folder.
4. Choose Auto or a fixed connection count.
5. Select Start Download.
6. The list and detail area show the filename, size, progress, speed, ETA, status, and connection count.

HyperDL accepts direct HTTP/HTTPS file URLs. It does not parse download
buttons or sign-in workflows from web pages.

### Connection modes

Choices are Auto, 1, 2, 4, 8, 16, 32, and 64. Auto selects a target by file
size, subject to confirmed range support, known file size, and the minimum
segment size. Auto is a sensible default. More connections do not guarantee
higher speed and may lead to HTTP 429 responses, throttling, or connection
errors.

### Pause and Resume

Pause and Resume are available only for segmented downloads with a known size
and server range support. Pause stops active workers and keeps the `.part`
file and each segment's downloaded offset. Before Resume, HyperDL probes the
source again and compares file size, ETag, Last-Modified, and range support.
It refuses to resume if compatibility cannot be confirmed, to avoid mixing
different file versions. The single-connection fallback cannot be paused or
resumed.

### Cancel

Cancel stops the current download. For segmented jobs it removes the
incomplete `.part` file and saves a Cancelled history state; that job can no
longer resume from the partial file. A single-connection download also
removes its `.part` file when cancelled or when it fails. Use Pause, not
Cancel, if you intend to continue later.

### `.part` files

A `.part` file contains incomplete data or data awaiting validation. Do not
rename it manually to the final filename. After checking the ranges, segment
states, and file length, HyperDL renames it to the final file.
Pausing a segmented download retains its `.part` file; manually deleting the
file invalidates recovery for that job. Cancel differs from Pause: Cancel
removes the job's incomplete `.part` file.

### Crash recovery

Segmented downloads use `.part` for file data and SQLite for URLs, destination
paths, status, and segment offsets. After restarting, HyperDL does not start
jobs automatically. Select a Recoverable job from the list and choose Resume.
The source is probed again before resuming. Normal uninstall does not
automatically delete SQLite or `.part` data.

### Opening the download folder

Select a job in the list and choose Open Download Folder. If no job is
selected, HyperDL opens the default folder from Settings. You can also browse
to a custom destination in File Explorer.

## Uninstallation

Inno Setup manages the real uninstaller created during installation. Its
filename is selected by the installer and may commonly be `unins000.exe`;
do not assume a fixed name or rename it.

### Method 1: Windows Settings

1. Open Windows Settings.
2. Go to Apps, then Installed apps.
3. Find HyperDL.
4. Select Uninstall and confirm.

### Method 2: Start Menu

1. Open Start and find HyperDL.
2. Select Uninstall HyperDL.
3. Confirm and follow the prompts.

### Method 3: Installation folder

You may run the Inno Setup-managed uninstaller from the HyperDL installation
folder. Its name is managed by Inno Setup and may be `unins000.exe`, but is
not guaranteed to be fixed. Windows Settings or the Start Menu shortcut is
recommended.

### What a normal uninstall keeps

A normal uninstall keeps downloaded files and local data to avoid accidental
data loss, including:

- Completed user downloads.
- Incomplete `.part` files outside the installation directory.
- The database, settings, download history, and recovery metadata in `%LOCALAPPDATA%\HyperDL`.
- User-selected download folders.

Uninstall removes application files from the installation directory, HyperDL
shortcuts, and the Windows uninstall registration. Do not store personal
downloads in the program installation directory; that directory is removed
with the application.

### Removing all local data

Only do this if you intend to delete local settings and download history:

1. Make sure important downloads are complete or backed up.
2. Uninstall HyperDL normally using one of the methods above.
3. Close HyperDL.
4. Enter `%LOCALAPPDATA%` in File Explorer, locate the `HyperDL` folder, and remove it manually.
5. If you also want to remove `.part` files in download folders, review and select each file manually.

Deleting `%LOCALAPPDATA%\HyperDL` removes download history, settings, and
recovery metadata. Deleting a `.part` file removes the ability to resume that
download. HyperDL does not automatically remove this user data.

## Building from source

### Requirements

- Windows 10/11 x64.
- .NET 8 SDK x64.
- Visual Studio with the .NET Desktop Development workload, or a compatible editor.
- PowerShell.
- Inno Setup 6 (only to compile the installer).

After obtaining the source, open PowerShell in the HyperDL project root
(the folder containing `HyperDL.sln` and `HyperDL.csproj`) and run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" restore HyperDL.sln
& "C:\Program Files\dotnet\dotnet.exe" build HyperDL.sln -c Release
& "C:\Program Files\dotnet\dotnet.exe" test HyperDL.sln -c Release --no-build
```

In Visual Studio, open `HyperDL.sln` and set HyperDL as the startup project.

### Run the application

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project HyperDL.csproj
```

## Publishing and building the installer

### Windows x64 publish

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

The script restores, builds Release, runs Release tests, then publishes
`win-x64` self-contained output with trimming disabled. The application is
written to `artifacts\publish\HyperDL\`; the executable is `HyperDL.exe`.

### Build Setup

Install Inno Setup 6, then run from the project root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script runs the publish flow, locates the Inno Setup compiler, and builds
the installer. Output is `installer\Output\HyperDL_Setup.exe`; the script
prints the file size and SHA-256. To run static checks on an existing
installer:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-installer.ps1
```

The installer uses the per-user location `%LOCALAPPDATA%\Programs\HyperDL`
and normally requires no administrator rights. The generated Setup is not
code-signed; verify release files and checksums before distribution.

## 🔧 Tech Stack

| Area | Technology |
|---|---|
| Desktop UI | WPF / XAML |
| Language | C# |
| Framework | .NET 8 |
| Networking | `HttpClient` |
| Async I/O | `async` / `await` / `Task` |
| Segmented writes | `System.IO.RandomAccess` |
| Persistence | Microsoft.Data.Sqlite |
| Testing | xUnit |
| Publishing | `dotnet publish` |
| Installer | Inno Setup 6 |
| Target | Windows x64 |

## Testing

Both the pre-change baseline and the latest test run completed with
**115 passed and 0 failed**. The Release build completed with **0 errors and
0 warnings**. These results are from this run and do not guarantee results
for other branches or future changes.

Automated coverage includes URL scheme validation, filename sanitization,
HTTP Range probing, Partial Content / `Content-Range`, segment boundaries,
connection planning, worker errors and cancellation, Pause/Resume offsets,
SQLite transactions, recovery state, and the global connection limiter.
Installation and uninstall checks are listed in
[docs/testing/INSTALL_UNINSTALL_TEST_CHECKLIST.md](docs/testing/INSTALL_UNINSTALL_TEST_CHECKLIST.md)
and require manual testing in an isolated Windows environment.

## Issue reports and contributions

Use the GitHub issue tracker to report problems and include the HyperDL
version, Windows version, reproduction steps, and error details without
secrets. Do not post passwords, tokens, cookies, private URLs, or personal
files.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidance and
[SECURITY.md](SECURITY.md) for security reporting.

## Security & Legal Use

- Download only content you are authorized to obtain and follow applicable terms and laws.
- DRM circumvention, paid-video decryption, and extraction from protected streams are not supported.
- HyperDL does not provide arbitrary command execution or download and run remote scripts.
- Downloaded EXE, MSI, BAT, CMD, PS1, and other files are not automatically executed by the download flow.
- A successful transfer does not prove a file is safe; verify its source and scan it with appropriate security tools.
- High connection counts can increase server load, trigger throttling, or reduce speed.

## Privacy

The download database is `%LOCALAPPDATA%\HyperDL\hyperdl.db`, and settings
are stored in `%LOCALAPPDATA%\HyperDL\settings.json`. The database contains
download URLs, destination paths, filenames, progress, and source validators;
download contents are written to the destination and `.part` file.

If a URL path or query string contains sensitive data, it may be saved in the
local database. Do not use URLs containing secrets. Normal uninstall keeps
`%LOCALAPPDATA%\HyperDL`, user downloads, and `.part` files outside the
installation directory. See “Removing all local data” for manual cleanup.

## Known Limitations

- Windows x64 and direct HTTP/HTTPS file URLs only.
- Some servers do not support ranges; those downloads use one connection, which cannot be paused and resumed.
- Fixed segments are created before download; dynamic splitting and adaptive connections are not implemented.
- More connections do not guarantee higher speed; servers may throttle, reject requests, or reduce throughput.
- Source-change detection depends on server-provided file size, ETag, Last-Modified, and similar information.
- FTP/FTPS, BitTorrent, a full multi-job scheduler, and automatic updates are unsupported.
- Unsigned executables or installers may trigger Windows SmartScreen.
- A Portable ZIP is not currently available.

## Roadmap

- [x] M0: Safety net, baseline, logging/CI foundations
- [x] M1: Reliability (retry, idle timeout, If-Range, source-change, buffer modes, disk checks, DB WAL/UPSERT, pending queue)
- [x] M2: Dynamic segmentation (scheduler, cooperative split, validation, adaptive)
- [x] M3: Usability + bilingual UI (multi-download, multi-select, search/filter, context menu, edit URL, delete, logs)
- [x] M4: Chrome extension bridge, protocol registration, publish/installer/CI updates
- [ ] M5: Tray/Toast/MVVM toolkit, HTTP/2 tuning, SHA-256 verification, scheduling, auto-update (deferred)

## 📜 License

HyperDL is licensed under the MIT License. Copyright © 2026 Kerby Chow.
See [LICENSE](LICENSE) for the full terms and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party license information.
