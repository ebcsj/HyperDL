# Changelog

All notable changes to HyperDL. Formats follow the release notes shipped with
each version; tags are published on the
[GitHub Releases page](https://github.com/ebcsj/HyperDL/releases).

## [1.4.0] - 2026-11-02

### Added
- Automatic updates: HyperDL downloads the newest release installer, verifies
  its SHA-256 against the release checksums, and asks before running it
  (opt-out setting).
- CLI commands over the single-instance pipe with JSON responses:
  `HyperDL.exe --list`, `--pause-all`, `--resume-all`, `--clear-completed`,
  and `--import <file>` (batch URL import).
- Dark theme with a runtime switch in Settings.
- Settings import/export (JSON) in Settings.
- Queue drag-and-drop reordering.
- Native messaging: the Chrome extension tries the app's native host first
  (no external-protocol prompt); enable it in Settings by pasting the
  extension ID from the extension's options page.

## [1.3.0] - 2026-11-01

### Added
- SHA-256 integrity: finished downloads are hashed automatically; right-click
  `Verify SHA-256…` compares against an expected value and `Copy SHA-256`
  copies the computed hash.
- Scheduled downloads: set a future start time per queued job
  (`Schedule…`); schedules persist across restarts (database schema v3).
- Per-segment progress bars and a completion summary in the details panel.
- Startup update check against GitHub Releases (opt-out; prompt-only).
- Optional HTTP/2 negotiation (`UseHttp2`, off by default).

### Changed
- The Chrome extension's `connections` parameter now overrides the per-download
  connection count, and `confirm=1` always shows the app confirmation dialog;
  `confirm=0` never bypasses the app's own confirmation setting.
- Fixed a v1.2 regression where a paused job could stay in the "Pausing" state.

## [1.2.0] - 2026-11-01

### Added
- System tray: minimize/close hides HyperDL to the tray (default on) with a
  tray menu (open, pause all, resume all, exit).
- Download notifications on completion and failure (default on).
- Opt-in clipboard URL monitoring (off by default) plus a Paste button.
- Optional shutdown prompt when the queue finishes (off by default).
- Application icon for the executable, tray, and shortcuts.

### Changed
- The global connection limiter parks waiters event-driven instead of polling.
- Removed unused localization strings; English/Traditional Chinese tables stay
  in parity (test-enforced).
- .NET analyzers enabled; build stays at zero warnings.
- Test suite grew from 115 to 154 tests (scheduler, bandwidth limiter, disk
  guard, download manager).

## [1.1.1] - 2026-10-31

### Fixed
- Three UI strings that displayed as raw keys
  (`NoCompletedMessage`, `UrlCopied`, `UrlUpdatedMessage`).
- Edit-URL dialog OK/Cancel buttons are localized.
- Recovery error messages are culture-neutral technical messages.
- The installer no longer ships debug symbols, and build paths are mapped
  out of shipped binaries (privacy).

### Added
- Version metadata in the project file; app/installer/extension aligned.
- Localization tests covering string-table parity (115 → 123 tests).

## [1.1.0] - 2026-10-27

### Added
- Concurrent segmented downloads with cooperative dynamic segmentation
  (≥ 64 MiB by default), buffer modes (`Auto`/`LowRam`/`Balanced`/`Fast`),
  retry with backoff and jitter, idle-timeout protection, `If-Range` on
  resume, disk preflight with FAT32 checks, bandwidth limiter, pending queue
  persistence with auto-resume, multi-select/context menu/search, bilingual
  UI, `hyperdl://` protocol registration, single-instance IPC, and the Chrome
  extension bridge.
