# HyperDL project structure

This guide explains where to find project files. The folders group related
work; the source namespaces and application behavior remain unchanged.

## Application

| Path | Purpose |
|---|---|
| `App.xaml`, `App.xaml.cs` | WPF application startup and shared application resources. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Main window layout, UI events, download-list coordination, and user feedback. |
| `Core/Downloading/` | Download engine, sessions, segments, progress, range layout, metadata, filename handling, and list entry model. |
| `Core/Networking/` | HTTP metadata probe, HTTP/HTTPS URL validation, and shared connection limiter. |
| `Core/Storage/` | SQLite download history/recovery and user settings persistence. |

Core types keep the existing `HyperDL.Core` namespace. The folder layout is
for navigation; it does not change public type names or require namespace
migration.

## Tests

| Path | Purpose |
|---|---|
| `HyperDL.Tests/Downloading/` | Download engine, range calculation, and filename behavior tests. |
| `HyperDL.Tests/Networking/` | URL validation, HTTP probing, and connection limiter tests. |
| `HyperDL.Tests/Storage/` | SQLite persistence and recovery tests. |
| `HyperDL.Tests/HyperDL.Tests.csproj` | Test dependencies and reference to the WPF project. |

Tests retain the `HyperDL.Tests` namespace. The application project excludes
the entire `HyperDL.Tests/` tree from its own compile items.

## Delivery and supporting files

| Path | Purpose |
|---|---|
| `scripts/publish.ps1` | Restore, build, test, and publish the self-contained Windows x64 app. |
| `scripts/build-installer.ps1` | Publish the app and compile the Inno Setup package. |
| `scripts/test-installer.ps1` | Read-only static checks for the installer script and Setup file. |
| `installer/HyperDL.iss` | Per-user installation, shortcuts, and uninstaller configuration. |
| `installer/Output/` | Generated Setup output; ignored by Git. |
| `docs/user-guides/` | User guides and troubleshooting in Traditional Chinese and English. |
| `docs/reference/` | Architecture reference notes, protocol support, security notes, and unsupported-feature boundaries. |
| `docs/testing/` | Manual installation and uninstall checklist. |
| `docs/images/` | Screenshot guidance and future application screenshots. |
| `.github/` | Issue and pull request templates. |

## Keep build output out of the repository

`bin/`, `obj/`, `artifacts/`, and `TestResults/` are generated during local
builds and publishing. They are not source folders and are ignored by Git.
The setup program is a release artifact, not required in the source
repository: build it from the scripts or attach the `.exe` to a GitHub
Release.

See the [documentation index](README.md), [architecture overview](ARCHITECTURE.md),
and [contribution guidelines](../CONTRIBUTING.md). 繁體中文版：
[專案結構說明](PROJECT_STRUCTURE.zh-TW.md)。
