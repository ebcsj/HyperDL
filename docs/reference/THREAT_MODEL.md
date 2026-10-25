# Threat model

## Protected assets

- User files and download destinations.
- SQLite download metadata and resumable `.part` files.
- User credentials; the application does not provide protocol credential
  fields.

## Trust boundaries

- Network responses are untrusted and written to a temporary file before
  finalization.
- HTTP requests use the platform TLS certificate validation defaults.
- Downloaded executables and scripts are not launched by the downloader.
- Download history is stored locally in the user's application data folder.

## Out of scope

DRM circumvention, arbitrary command execution, remote script execution,
BitTorrent, FTP/FTPS, plug-in loading, and automatic updates are not supported.

## Residual risks

Many simultaneous connections can burden a remote server; users should prefer
Auto and respect server throttling. A downloaded file can still be malicious
even when its transport succeeds; users should verify the source and scan
files before opening them.
