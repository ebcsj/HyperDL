# HyperDL Troubleshooting

[繁體中文](TROUBLESHOOTING.md) | [English](TROUBLESHOOTING.en.md)

## The URL is rejected

- Only absolute HTTP and HTTPS URLs are supported. Use a direct file URL, not
  a home page, sign-in page, FTP address, or magnet link.
- Check for whitespace, spelling errors, and embedded credentials. HyperDL
  does not provide fields for website cookies or authorization tokens.
- Some sites require sign-in, a CAPTCHA, or a browser session during probing
  or download. Those workflows are outside the supported scope.

## The download fails or remains at zero speed

- Confirm that the server is reachable, the URL is still valid, and the file
  is available from your current network.
- Check whether Windows Firewall, a proxy, VPN, or network filter is blocking
  the connection.
- Make sure the destination drive has enough free space and the current user
  can write to the download folder.
- If the server does not support HTTP Range, HyperDL falls back to one
  connection. This is expected, although it may be slower than segmented
  downloading.
- An unknown total means the server did not provide a usable `Content-Length`.
  The download can still proceed, but percentage and ETA may be unavailable.

## Pause or resume is unavailable

Pause/Continue is supported only for segmented downloads with confirmed range
support and a known file size. The single-connection fallback cannot resume.
If the server rejects Range, the length is unknown, or probing is inconclusive,
allow the download to finish or cancel and start it again.

Before resuming, HyperDL probes the source and compares available file size,
ETag, Last-Modified, and range information. If the source changed or cannot be
validated again, the job stops to avoid combining data from different
versions.

## The file or `.part` temporary file is missing

- Check the configured download folder, or select a job and use
  `開啟下載資料夾` (Open Download Folder).
- If a file with the same name already exists, the completed file may use a
  numbered name.
- `.part` is an incomplete temporary file. Cancelling removes that job's
  incomplete content. When deleting a recoverable record, the UI asks whether
  to remove its `.part` file too. Do not rename or move a temporary file while
  it is in use.
- Single-connection downloads do not save a resumable session. If the
  application exits unexpectedly, that incomplete content cannot be resumed
  from the recovery list.

## Download history or settings cannot be loaded

HyperDL stores settings and download metadata in `%LOCALAPPDATA%\HyperDL`.
If the UI reports a database or settings error, close the application and
back up that folder first. Then check the user's disk space and folder
permissions. Do not delete the SQLite main file, `-wal`, or `-shm` files while
the application is running.

## Build or installation problems

- Development builds require Windows x64 and the .NET 8 SDK. Run
  `dotnet restore HyperDL.sln`, then
  `dotnet build HyperDL.sln -c Release`.
- Building Setup requires Inno Setup 6. If the compiler cannot be found,
  install it and retry `scripts\build-installer.ps1`.
- Because Setup is unsigned, Windows may show a SmartScreen warning. Run only
  installers obtained from a source you trust.
- If Setup or the application will not start, record the Windows version,
  error message, and reproduction steps. Remove private information from URLs
  before sharing logs.

See [supported protocols](../reference/PROTOCOLS.md),
the [user guide](USER_GUIDE.en.md), and the
[installation test checklist](../testing/INSTALL_UNINSTALL_TEST_CHECKLIST.md)
for more information.
