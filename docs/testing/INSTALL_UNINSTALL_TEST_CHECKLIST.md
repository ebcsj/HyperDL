# Install and uninstall manual test checklist

Use a disposable Windows x64 test account or virtual machine. Do not perform
destructive checks against real user downloads or recovery data.

## Installation

- [ ] Clean per-user installation.
- [ ] Upgrade installation over an existing version.
- [ ] Default install location is `%LOCALAPPDATA%\Programs\HyperDL`.
- [ ] Custom install location works.
- [ ] Desktop shortcut is created when the task is selected.
- [ ] Desktop shortcut is not created when the task is not selected.
- [ ] Start Menu HyperDL shortcut launches the application.
- [ ] Start Menu `Uninstall HyperDL` shortcut opens the managed uninstaller.
- [ ] HyperDL appears in Windows Installed Apps / Apps and Features.
- [ ] Application icon, version, and publisher are displayed as expected.
- [ ] Installed application files are present.
- [ ] No removed integration or host files are installed.

## Download workflows

- [ ] Add a direct HTTP URL.
- [ ] Add a direct HTTPS URL.
- [ ] Auto connection mode.
- [ ] Fixed connection mode.
- [ ] Pause an eligible segmented download.
- [ ] Resume a paused download.
- [ ] Cancel a download and verify the documented partial-file behavior.
- [ ] Restart the application and check crash-recovery behavior.
- [ ] Open the selected download folder.
- [ ] Verify the completed file and its expected length.

## Uninstallation

- [ ] Uninstall from Windows Settings.
- [ ] Uninstall from the Start Menu shortcut.
- [ ] Confirm the uninstall confirmation dialog.
- [ ] Program files are removed from the installation directory.
- [ ] Application and uninstall shortcuts are removed.
- [ ] Windows Installed Apps entry is removed.
- [ ] Completed user downloads are retained.
- [ ] `.part` files outside the installation directory are retained.
- [ ] `%LOCALAPPDATA%\HyperDL` settings and history are retained.
- [ ] Reinstall works after uninstall.
- [ ] Upgrade installation works.

## Optional full local-data removal

- [ ] Back up any needed downloads and recovery state first.
- [ ] Uninstall HyperDL normally.
- [ ] Close all HyperDL processes.
- [ ] Manually remove `%LOCALAPPDATA%\HyperDL` only when intentionally deleting
      settings, download history, and recovery metadata.
- [ ] Independently review any `.part` files in user-selected download
      folders; deleting one removes that job's resume data.
- [ ] Verify that unrelated user downloads remain untouched.

Traditional Chinese: [安裝與解除安裝人工測試清單](INSTALL_UNINSTALL_TEST_CHECKLIST.zh-TW.md).
