# HyperDL Installation Guide

This guide explains how to download and install the HyperDL Windows x64 installer.

## Download

1. Download `HyperDL_Setup.exe` from the official GitHub Release assets.
2. Make sure you download it only from the official GitHub Release page.
3. Optional: download `HyperDL_<version>_win-x64_portable.zip` from the same release for a portable, no-install copy.

## Install

1. Run `HyperDL_Setup.exe`.
2. Follow the installer setup instructions.
3. By default, HyperDL will be installed for the current user to `%LocalAppData%\Programs\HyperDL`.
4. You can choose to create a desktop shortcut during installation.

## Update an Existing Installation

1. Download the latest `HyperDL_Setup.exe` from the GitHub Release.
2. Run the installer. It will update your existing installation.
3. Launch HyperDL after installation completes.

## Notes

- HyperDL is Windows x64 only.
- The installer is not code-signed. Windows SmartScreen may display a warning on first run.
- Each release includes `SHA256SUMS.txt` with checksums for the installer and the portable ZIP; verify before running the installer.
- HyperDL uses a per-user installation and does not require administrator privileges by default.
