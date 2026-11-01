# HyperDL project structure

This guide explains where to find project files. The folders group related
work; the source namespaces and application behavior remain unchanged.

## Application

| Path | Purpose |
|---|---|
| `App.xaml`, `App.xaml.cs` | WPF application startup and shared application resources. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Main window layout, UI events, download-list coordination, and user feedback. |
| `Core/Downloading/` | Download engine, manager, job model, sessions, segments, scheduler, retry and bandwidth limiting, progress, range layout, metadata, file hashing, and filename handling. |
| `Core/Networking/` | HTTP metadata probe, HTTP/HTTPS URL validation, shared connection limiter, and update version comparison. |
| `Core/Storage/` | SQLite download history/recovery and user settings persistence. |
| `Localization/` | Runtime localization manager, XAML markup extension, and the English/Traditional Chinese string tables. |
| `Platform/` | `hyperdl://` protocol + native-messaging host registration, IPC protocol, CLI console output, update check/download. |
| `Themes/` | Light and dark theme resource dictionaries. |
| `Assets/` | Application icon (`app.ico`) used by the executable and tray. |
| `TextPromptWindow.cs` | Code-only modal prompt used for URL editing. |

Core types keep the existing `HyperDL.Core` namespace. The folder layout is
for navigation; it does not change public type names or require namespace
migration.

## Tests

| Path | Purpose |
|---|---|
| `HyperDL.Tests/Downloading/` | Download engine, range calculation, and filename behavior tests. |
| `HyperDL.Tests/Networking/` | URL validation, HTTP probing, and connection limiter tests. |
| `HyperDL.Tests/Storage/` | SQLite persistence and recovery tests. |
| `HyperDL.Tests/Localization/` | String-table key parity and shell-string coverage tests. |
| `HyperDL.Tests/HyperDL.Tests.csproj` | Test dependencies and reference to the WPF project. |

Tests retain the `HyperDL.Tests` namespace. The application project excludes
the entire `HyperDL.Tests/` tree from its own compile items.

## Delivery and supporting files

| Path | Purpose |
|---|---|
| `scripts/publish.ps1` | Restore, build, test, and publish the self-contained Windows x64 app. |
| `scripts/build-installer.ps1` | Publish the app and compile the Inno Setup package. |
| `scripts/test-installer.ps1` | Read-only static checks for the installer script and Setup file. |
| `scripts/test-server.ps1` | Loopback HTTP range test server with throttle, delay, rate-limit, and failure flags. |
| `scripts/benchmark.ps1` | Raw loopback HTTP range throughput benchmark. |
| `scripts/build-extension.ps1` | Validates and packages the Chrome extension. |
| `scripts/README.md` | Usage notes for the test server and benchmark. |
| `installer/HyperDL.iss` | Per-user installation, shortcuts, and uninstaller configuration. |
| `installer/Output/` | Generated Setup output; ignored by Git. |
| `extension/` | Manifest V3 Chrome extension source, README, and privacy policy. |
| `docs/user-guides/` | User guides and troubleshooting in Traditional Chinese and English. |
| `ROADMAP.md` | Improvement roadmap: shipped scope per version and candidate backlog (Traditional Chinese). |
| `RELEASE_NOTES_v1.2.0.md` | Release notes for the current version (bilingual). |
| `docs/reference/` | Architecture reference notes, protocol support, security notes, and unsupported-feature boundaries. |
| `docs/testing/` | Manual installation and uninstall checklist. |
| `docs/images/` | Screenshot guidance and future application screenshots. |
| `.github/` | Issue and pull request templates, release checklist, and dependabot config. |
| `.github/workflows/` | CI build/test, release, and extension packaging workflows. |

## Keep build output out of the repository

`bin/`, `obj/`, `artifacts/`, and `TestResults/` are generated during local
builds and publishing. They are not source folders and are ignored by Git.
The setup program is a release artifact, not required in the source
repository: build it from the scripts or attach the `.exe` to a GitHub
Release.

See the [documentation index](README.md), [architecture overview](ARCHITECTURE.md),
and [contribution guidelines](../CONTRIBUTING.md). 繁體中文版：
[專案結構說明](PROJECT_STRUCTURE.zh-TW.md)。
