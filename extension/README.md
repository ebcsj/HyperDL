# HyperDL Chrome Extension

A Manifest V3 browser extension that hands direct HTTP/HTTPS download links to
the HyperDL desktop download manager on Windows.

The extension is intentionally small. It does not inject content scripts, read
page content, collect analytics, or make network requests. Its only integration
is the local `hyperdl://` protocol handler registered by the HyperDL installer.

## Requirements

- Windows with HyperDL installed.
- A current Chromium-based browser (Chrome, Edge, Brave, or another Chromium
  browser) with Manifest V3 support.
- The `hyperdl://` protocol registered by the HyperDL installer. If the app was
  installed without protocol registration, reinstall or repair HyperDL.

## Install with "Load unpacked"

1. Open `chrome://extensions` (or `edge://extensions`).
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose this `extension` folder, or a folder where
   `artifacts/extension/HyperDL-Chrome.zip` was extracted.

Do not select the ZIP file itself; Chrome can only load an extracted folder.

After loading, pin the HyperDL icon to the toolbar. The extension is now ready
to send links.

## Usage

### Toolbar popup

1. Open the page or direct file link you want to download.
2. Click the HyperDL toolbar icon.
3. Review the current tab URL and choose **Connections**
   (`Auto`, `1`, `2`, `4`, `8`, `16`, `32`, `64`).
4. Click **Download with HyperDL**.
5. If Chrome asks whether to open HyperDL, enable **Always allow** and confirm.
6. Confirm the download in HyperDL if the app asks for confirmation.

The popup warns when the current URL does not look like a direct file link.
HyperDL only supports direct HTTP/HTTPS file links (for example
`https://example.com/archive.zip`). It cannot download a video page such as a
YouTube watch page. You can still send a non-file URL, but the download will
usually fail.

### Link context menu

Right-click any HTTP/HTTPS link and choose **Download with HyperDL**. The link
is sent immediately using the default connections setting. The app's
confirmation setting still applies.

Only `http://` and `https://` links are sent. Other schemes are ignored.

## Settings

Open the extension's **Settings** page from the popup footer or from the
Extensions page. Settings are stored with `chrome.storage.sync`:

| Key | Values | Default | Meaning |
| --- | --- | --- | --- |
| `connections` | `auto`, `1`, `2`, `4`, `8`, `16`, `32`, `64` | `auto` | Default connection count passed to HyperDL |
| `confirmInApp` | `true`, `false` | `true` | Ask HyperDL to confirm before adding the download |

The options page also displays the installed extension version.

The popup connection selector applies to the current handoff only. The options
page controls the default used by new popups and by the context menu.

## Protocol contract

The extension launches this URL:

```text
hyperdl://add?url=<url-encoded-http-or-https-url>&connections=<auto-or-number>&confirm=<1-or-0>
```

Example:

```text
hyperdl://add?url=https%3A%2F%2Fexample.com%2Ffile.zip&connections=8&confirm=1
```

The desktop app is responsible for validating the URL again, limiting launches
to HTTP/HTTPS, and showing its confirmation dialog. The extension never sends
non-HTTP(S) schemes.

## Permissions

| Permission | Why it is needed |
| --- | --- |
| `contextMenus` | Adds the "Download with HyperDL" link context menu |
| `activeTab` | Reads the current tab URL after the user opens the popup |
| `storage` | Stores the two settings in `chrome.storage.sync` |

The extension does not request host permissions, does not declare content
scripts, and does not use the `tabs` permission. It has a strict extension-page
content security policy (`script-src 'self'; object-src 'self'`) and loads no
remote code.

See [PRIVACY.md](PRIVACY.md) for the privacy policy.

## Build and package

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-extension.ps1
```

The script:

1. Generates `icons/icon16.png`, `icons/icon48.png`, and `icons/icon128.png`
   with `System.Drawing`.
2. Parses every JSON file in the extension folder.
3. Checks that every manifest- and HTML-referenced file exists.
4. Enforces the security constraints (Manifest V3, allowed permissions only,
   no content scripts or host permissions, strict CSP, no network APIs).
5. Creates `artifacts/extension/HyperDL-Chrome.zip` with `manifest.json` at the
   archive root.

For Chrome Web Store submission, upload `HyperDL-Chrome.zip`. For local use,
extract it and use **Load unpacked**.

## Troubleshooting

### Chrome does not open HyperDL

- Confirm HyperDL is installed and has been started at least once.
- Confirm the protocol registration exists:
  `HKEY_CURRENT_USER\Software\Classes\hyperdl`.
- Reinstall or repair HyperDL so the installer can register the protocol.
- Restart the browser after installing or repairing the app.
- Check Windows **Settings > Apps > Default apps** for the `hyperdl` protocol.

### Chrome shows "Open HyperDL?" every time

Chrome shows an external-protocol confirmation the first time. Select the
"Always allow" option before confirming to avoid the prompt for later links.
An extension cannot suppress this browser or operating-system prompt.

### The popup says the page cannot be sent

Browser-internal pages (`chrome://`, `edge://`, `about:`), the Chrome Web Store,
`file://` pages, and similar restricted pages do not expose their URL to the
extension. Open a normal `http://` or `https://` page instead.

### The download fails in HyperDL

The link may not be a direct file URL, may require cookies or a login session,
or may be generated by JavaScript. HyperDL only supports direct HTTP/HTTPS file
links. Copy the final file URL or use the site's own download option, then paste
or drag the direct link into HyperDL.

## Version

Extension version: **1.0.0**

This first version uses the custom protocol bridge. Native Messaging may be
evaluated later for a tighter integration.
