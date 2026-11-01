# HyperDL Chrome Extension

This guide explains how to download and load the HyperDL Chrome extension for browser integration with the HyperDL app.

## Download

1. Download `HyperDL-Chrome.zip` from the official GitHub Release assets, or use `artifacts/extension/HyperDL-Chrome.zip` from the source package.
2. Extract the ZIP file to a local folder (keep this folder after loading the extension).

## Load in Chrome (Developer Mode)

1. Open Chrome and go to `chrome://extensions/`.
2. Enable **Developer mode** in the top-right corner.
3. Click **Load unpacked**.
4. Select the extracted folder containing the extension files (`manifest.json` must be present).
5. The HyperDL extension will appear in the extensions list.

## Usage

1. Make sure HyperDL is installed and running.
2. Click the HyperDL extension icon to send supported download links to HyperDL via the `hyperdl://` protocol.
3. When prompted, allow HyperDL to open the `hyperdl://` link.

## Notes

- This extension is not published to the Chrome Web Store. It must be loaded in Developer mode.
- The extension works with Chrome, Chromium-based browsers, and Microsoft Edge (use `edge://extensions/` with Developer mode).
- HyperDL must be installed for the protocol handler (`hyperdl://`) to work properly.
- If updates are released, download the new ZIP and replace the extension folder, then click **Reload** on `chrome://extensions/`.
