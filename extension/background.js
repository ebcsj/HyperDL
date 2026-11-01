"use strict";

/**
 * HyperDL Chrome bridge.
 *
 * The extension deliberately has no content scripts and makes no network
 * requests. Its only integration point is the local hyperdl:// protocol
 * handler registered by the HyperDL installer.
 */

const MENU_ID = "hyperdl-download-link";
const MENU_TITLE = "Download with HyperDL";
const CONNECTIONS = ["auto", "1", "2", "4", "8", "16", "32", "64"];
const DEFAULT_SETTINGS = Object.freeze({
  connections: "auto",
  confirmInApp: true,
});

function isHttpUrl(value) {
  if (typeof value !== "string" || value.length === 0) {
    return false;
  }

  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function normalizeSettings(value) {
  const stored = value && typeof value === "object" ? value : {};
  return {
    connections: CONNECTIONS.includes(stored.connections)
      ? stored.connections
      : DEFAULT_SETTINGS.connections,
    confirmInApp:
      typeof stored.confirmInApp === "boolean"
        ? stored.confirmInApp
        : DEFAULT_SETTINGS.confirmInApp,
  };
}

async function loadSettings() {
  try {
    const stored = await chrome.storage.sync.get(DEFAULT_SETTINGS);
    return normalizeSettings(stored);
  } catch (error) {
    console.warn("HyperDL: could not read settings, using defaults.", error);
    return { ...DEFAULT_SETTINGS };
  }
}

function buildProtocolUrl(url, settings) {
  const confirm = settings.confirmInApp ? "1" : "0";
  const encodedUrl = encodeURIComponent(url);
  const encodedConnections = encodeURIComponent(settings.connections);
  return `hyperdl://add?url=${encodedUrl}&connections=${encodedConnections}&confirm=${confirm}`;
}

async function ensureContextMenu() {
  await new Promise((resolve) => {
    chrome.contextMenus.removeAll(() => {
      chrome.contextMenus.create(
        {
          id: MENU_ID,
          title: MENU_TITLE,
          contexts: ["link"],
        },
        () => {
          if (chrome.runtime.lastError) {
            console.warn(
              "HyperDL: could not create the context menu.",
              chrome.runtime.lastError.message
            );
          }
          resolve();
        }
      );
    });
  });
}

async function ensureDefaultSettings() {
  try {
    const stored = await chrome.storage.sync.get(null);
    const patch = {};

    if (!CONNECTIONS.includes(stored.connections)) {
      patch.connections = DEFAULT_SETTINGS.connections;
    }
    if (typeof stored.confirmInApp !== "boolean") {
      patch.confirmInApp = DEFAULT_SETTINGS.confirmInApp;
    }
    if (Object.keys(patch).length > 0) {
      await chrome.storage.sync.set(patch);
    }
  } catch (error) {
    console.warn("HyperDL: could not initialize settings.", error);
  }
}

/**
 * Try native messaging first (no external-protocol prompt, works even when
 * no tab is available). Returns true when the app accepted the download.
 */
async function sendViaNativeMessaging(url, settings) {
  if (!chrome.runtime.sendNativeMessage) {
    return false;
  }

  try {
    const response = await chrome.runtime.sendNativeMessage(
      "com.ebcsj.hyperdl",
      {
        type: "add",
        url: url,
        connections: settings.connections === "auto" ? 0 : Number(settings.connections),
        confirm: settings.confirmInApp ? 1 : 0,
      }
    );
    return Boolean(response && response.ok);
  } catch (error) {
    console.warn(
      "HyperDL: native messaging unavailable, falling back to hyperdl://.",
      error
    );
    return false;
  }
}

async function handleLinkClick(info, tab) {
  if (info.menuItemId !== MENU_ID) {
    return;
  }

  const linkUrl = typeof info.linkUrl === "string" ? info.linkUrl : "";
  if (!isHttpUrl(linkUrl)) {
    console.warn("HyperDL: ignored a non-HTTP(S) link.");
    return;
  }

  const settings = await loadSettings();
  if (await sendViaNativeMessaging(linkUrl, settings)) {
    return;
  }

  const protocolUrl = buildProtocolUrl(linkUrl, settings);
  await openHyperDL(protocolUrl, tab && tab.id);
}

chrome.runtime.onInstalled.addListener((details) => {
  void ensureContextMenu();
  if (details.reason === "install") {
    void ensureDefaultSettings();
  }
});

chrome.runtime.onStartup.addListener(() => {
  void ensureContextMenu();
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  void handleLinkClick(info, tab);
});

// Recreate the menu whenever the service worker starts. Menus persist across
// worker shutdowns, but rebuilding keeps the item available after a reload.
void ensureContextMenu();
