# HyperDL User Guide

[繁體中文](USER_GUIDE.md) | [English](USER_GUIDE.en.md)

HyperDL is a Windows desktop download manager for direct HTTP and HTTPS file
URLs. It does not extract protected streams from video services or launch
downloaded files automatically.

## Install and launch

1. Obtain HyperDL Setup only from a source you trust.
2. Run `HyperDL_Setup.exe` and follow the Setup prompts. Administrator
   privileges are not required. The installer is currently unsigned.
3. Start HyperDL from the Start Menu.
4. To run from source, install the .NET 8 SDK on Windows x64 and run this from
   the project root:

   ```powershell
   dotnet run --project HyperDL.csproj
   ```

## Start a download

1. Enter a complete direct-file `http://` or `https://` URL in the `URL` field.
   Web pages, FTP URLs, magnet links, and protected content requiring browser
   authentication are not supported.
2. Choose `Auto` or a connection count. Auto selects a target from the known
   file size. If the server does not support byte ranges, the size is unknown,
   or the file is too small, HyperDL uses a single connection.
3. Select `Add download`. HyperDL probes the server before downloading.
4. Check the progress area and download list for the file name, size,
   percentage, speed, ETA, status, and connection count. If the server does
   not provide the total length, the total and percentage may be unknown.
5. The completed file is saved to the configured download folder, which
   defaults to the current Windows user's `Downloads` folder. Select a job and
   use `Open folder` to open its containing folder.

HyperDL prefers the file name supplied by the server, then falls back to the
URL path and sanitizes unsafe characters. If a target name already exists, a
numbered alternative is used.

## Connections, pause, and cancel

- Available modes are `Auto`, 1, 2, 4, 8, 16, 32, and 64. The actual segment
  count is constrained by file size, server support, and the global connection
  limit. More connections do not guarantee higher speed.
- `Pause` and `Resume` are available only for segmented downloads with
  confirmed range support and a known file size. Before resuming, HyperDL
  checks the source again and refuses to continue if its size or available
  validators indicate that it changed.
- The single-connection fallback cannot be paused or resumed. Cancelling
  stops the job and keeps its `.part` file, so a cancelled segmented job can
  be resumed from the list later.
- Removing a job from the list keeps its `.part` file. Use
  `Delete file and remove` to delete the job together with its downloaded and
  partial data; confirm the data is no longer needed first.

## Settings and download history

Expand `Settings` to configure the default download folder, Auto/fixed
connection count, maximum concurrent downloads, buffer mode
(`Auto`, `LowRam`, `Balanced`, `Fast`), speed limit, dynamic segmentation, and
language. Buffer sizes stay inside a global 64 MiB network-buffer budget;
they are not a total application memory limit. The maximum
concurrent-download setting controls how many jobs run at the same time.

Settings are stored in `%LOCALAPPDATA%\HyperDL\settings.json`. Metadata for
recoverable segmented downloads is stored in
`%LOCALAPPDATA%\HyperDL\hyperdl.db`; the database does not contain file
contents. After an unexpected shutdown, paused and interrupted jobs resume on
startup when auto-resume is enabled (the default); otherwise select a
recoverable job in the list and choose `Resume`. The source is checked again
before resuming. A single-connection download does not create a recoverable
session.

Normal uninstall removes the application but keeps user downloads, `.part`
files, settings, and history under `%LOCALAPPDATA%\HyperDL`. Back up any data
before manually removing it.

## Safe use

Only download content you are authorized to obtain, and follow website terms
and applicable law. Do not put passwords, cookies, or tokens in URLs.
HyperDL does not automatically run downloaded EXE, MSI, BAT, CMD, PS1, or
other files. Verify the source and scan a file with suitable security tools
before opening it.

For help, see [Troubleshooting](TROUBLESHOOTING.en.md) and
[Architecture](../ARCHITECTURE.md).
