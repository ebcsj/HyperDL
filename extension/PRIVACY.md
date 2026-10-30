# HyperDL Chrome Extension Privacy Policy

Effective date: October 26, 2026

This privacy policy applies to the HyperDL Download Bridge browser extension.

## Summary

The extension does not collect, sell, or transmit personal data. It has no
analytics, telemetry, advertising, or remote servers. It sends a URL to the
locally installed HyperDL desktop application only when you explicitly choose
to download it.

## What the extension accesses

- **Current tab URL** - Read only when you open the extension popup, so it can
  be shown to you and sent to HyperDL at your request.
- **Link URL** - Read only for the link you right-click when you choose
  **Download with HyperDL**.
- **Settings** - The default connection count and confirmation preference are
  stored with `chrome.storage.sync`.

The extension does not read page content, form fields, cookies, passwords,
browsing history, bookmarks, or files on your computer. It does not inject
content scripts.

## How URLs are used

When you click **Download with HyperDL** or use the link context menu, the
extension constructs a local `hyperdl://add` URL containing the link to
download. It passes that URL to the operating system's registered HyperDL
protocol handler. The URL goes only to the HyperDL app on your computer. The
developer of the extension does not receive it.

The extension does not store the URLs it sends. It keeps only the two settings
described above.

## Data collection and sharing

- No personal data is collected by the extension.
- No data is sold or shared with third parties.
- No analytics, crash reporting, tracking pixels, or advertising SDKs are used.
- No remote code is loaded. All code is packaged in the extension.

## Chrome sync

Settings stored with `chrome.storage.sync` may be synchronized across devices
by your browser vendor if you are signed in and have sync enabled. That
synchronization is controlled by your browser account settings, not by the
extension. The developer does not have access to it.

## Permissions

- `contextMenus` - Adds the "Download with HyperDL" link context menu.
- `activeTab` - Allows the popup to read the current tab URL after you open it.
- `storage` - Saves the extension settings.

No host permissions or content-script access are requested.

## Security

The extension never sends non-HTTP(S) URLs. The HyperDL desktop app is expected
to validate the URL again and show its own confirmation dialog by default. You
can review the extension source in the HyperDL repository under `extension/`.

## Children's privacy

The extension does not collect data from anyone, including children.

## Changes to this policy

If a future version changes how data is handled, this policy will be updated
before the change is released. Material changes will be documented in the
extension README.

## Contact

For questions or security reports, use the HyperDL project's issue tracker or
security policy.
