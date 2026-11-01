# Supported protocols

## Implemented

- HTTP and HTTPS direct file downloads.
- HTTP Range probing and segmented requests when the server confirms byte
  ranges and provides a known positive length.
- Cooperative dynamic segment splitting for eligible range-supported
  downloads (64 MiB or larger by default).
- Single-connection fallback when segmentation cannot be safely selected.

## Not implemented

- FTP or FTPS. Do not enter credentials into an HTTP URL or expect FTP URLs to
  work.
- BitTorrent, magnet links, or tracker operations.
- Update feeds or update package installation.

DRM or protected-service access controls are not bypassed.
