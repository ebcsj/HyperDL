# Release Checklist

Use this checklist for every public HyperDL release. Release tags must look like `vX.Y.Z` (for example `v1.0.0`).

## 1. Version bump

- [ ] Update the version in `HyperDL.csproj` (`<Version>`/`<FileVersion>`, if present).
- [ ] Update `#define AppVersion` in `installer/HyperDL.iss` (currently `1.0.0.0`).
- [ ] Update the extension version in its manifest under `extension/`.
- [ ] Grep the docs for old version strings and update them.
- [ ] Confirm the release tag matches the version (`vX.Y.Z`).

## 2. Tests and build

- [ ] `dotnet restore HyperDL.sln`
- [ ] `dotnet build HyperDL.sln -c Release`
- [ ] `dotnet test HyperDL.sln -c Release --no-build`
- [ ] Confirm the `Build` workflow is green on `main`/`master`.

## 3. Installer

- [ ] Run `scripts/build-installer.ps1` on a clean Windows machine.
- [ ] Install per-user, launch the app, and verify the Start Menu shortcut.
- [ ] Verify the optional desktop shortcut task.
- [ ] Uninstall and confirm no application files are left behind.
- [ ] Optionally run `scripts/test-installer.ps1`.

## 4. Portable build and extension

- [ ] Verify `artifacts\publish\HyperDL\HyperDL.exe` launches without the SDK installed.
- [ ] Run `scripts/build-extension.ps1`.
- [ ] Load the unpacked extension in Chrome/Edge and smoke-test it.
- [ ] Confirm the extension zip contains the expected files and the manifest version is current.

## 5. Checksums

- [ ] Generate SHA-256 hashes for the installer and portable zip.
- [ ] Confirm `SHA256SUMS.txt` lists both assets with the exact release file names.
- [ ] Re-verify the downloaded assets after publishing.

## 6. Code signing

- [ ] If a certificate is available, sign the installer and `HyperDL.exe`.
- [ ] Verify signatures with `Get-AuthenticodeSignature` and confirm `Status` is `Valid`.
- [ ] Never commit a `.pfx`, certificate password, or other signing secret.
- [ ] If shipping unsigned, mention the expected SmartScreen warning in the release notes.

## 7. Release notes and docs

- [ ] Draft release notes: highlights, fixes, breaking changes, upgrade notes, known issues.
- [ ] Update `README.md`, `README.zh-TW.md`, and relevant `docs/` pages.
- [ ] Update `THIRD_PARTY_NOTICES.md` if dependencies changed.
- [ ] Verify the release will include the installer `.exe`, portable `.zip`, and `SHA256SUMS.txt`.

## 8. Publish

- [ ] Push the tag: `git tag vX.Y.Z` then `git push origin vX.Y.Z`.
- [ ] Or manually run the `Release` workflow and provide the tag input.
- [ ] Confirm the `Release` workflow succeeded.
- [ ] Confirm the GitHub Release is public and has all three assets.
- [ ] Download the assets from the release page and re-check the checksums.
