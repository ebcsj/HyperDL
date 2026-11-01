"use strict";

const CONNECTIONS = ["auto", "1", "2", "4", "8", "16", "32", "64"];
const DEFAULT_SETTINGS = Object.freeze({
  connections: "auto",
  confirmInApp: true,
});

// Common direct-download extensions. This is only used to decide whether to
// show an advisory warning; non-matching URLs can still be sent.
const DIRECT_EXTENSIONS = new Set([
  "7z",
  "ace",
  "apk",
  "avi",
  "bin",
  "bz2",
  "cab",
  "csv",
  "deb",
  "dmg",
  "doc",
  "docx",
  "epub",
  "exe",
  "flac",
  "gif",
  "gz",
  "img",
  "iso",
  "jar",
  "jpeg",
  "jpg",
  "m4a",
  "m4v",
  "mkv",
  "mov",
  "mp3",
  "mp4",
  "mpeg",
  "mpg",
  "msi",
  "odt",
  "ogg",
  "pdf",
  "png",
  "ppt",
  "pptx",
  "rar",
  "rpm",
  "tar",
  "tgz",
  "tif",
  "tiff",
  "torrent",
  "txt",
  "wav",
  "webm",
  "webp",
  "wim",
  "xls",
  "xlsx",
  "xz",
  "zip",
  "zst",
]);

const elements = {
  form: document.getElementById("download-form"),
  url: document.getElementById("tab-url"),
  notice: document.getElementById("url-notice"),
  connections: document.getElementById("connections"),
  send: document.getElementById("send"),
  status: document.getElementById("status"),
  version: document.getElementById("version"),
  options: document.getElementById("open-options"),
};

let currentTabUrl = "";
let confirmInApp = DEFAULT_SETTINGS.confirmInApp;

function parseHttpUrl(value) {
  if (typeof value !== "string" || value.length === 0) {
    return null;
  }

  try {
    const url = new URL(value);
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      return null;
    }
    return url;
  } catch {
    return null;
  }
}

function isDirectFileUrl(value) {
  const url = parseHttpUrl(value);
  if (!url) {
    return false;
  }

  const lastSegment = url.pathname.split("/").pop() || "";
  const match = /\.([a-z0-9]{1,8})$/i.exec(lastSegment);
  return Boolean(match && DIRECT_EXTENSIONS.has(match[1].toLowerCase()));
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

async function getSettings() {
  try {
    const stored = await chrome.storage.sync.get(DEFAULT_SETTINGS);
    return normalizeSettings(stored);
  } catch (error) {
    console.warn("HyperDL: could not read settings, using defaults.", error);
    return { ...DEFAULT_SETTINGS };
  }
}

async function getActiveTab() {
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    return tabs && tabs.length > 0 ? tabs[0] : null;
  } catch (error) {
    console.warn("HyperDL: could not read the active tab.", error);
    return null;
  }
}

function populateConnections() {
  for (const value of CONNECTIONS) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value === "auto" ? "Auto" : value;
    elements.connections.appendChild(option);
  }
}

function showNotice(message, kind) {
  elements.notice.textContent = message;
  elements.notice.className = `notice ${kind}`;
  elements.notice.hidden = false;
}

function hideNotice() {
  elements.notice.textContent = "";
  elements.notice.className = "notice hidden";
  elements.notice.hidden = true;
}

function setStatus(message, kind) {
  elements.status.textContent = message;
  elements.status.className = kind ? `status ${kind}` : "status";
}

function buildProtocolUrl(url, connections, confirm) {
  const confirmValue = confirm ? "1" : "0";
  const encodedUrl = encodeURIComponent(url);
  const encodedConnections = encodeURIComponent(connections);
  return `hyperdl://add?url=${encodedUrl}&connections=${encodedConnections}&confirm=${confirmValue}`;
}

/**
 * An <a> click inside the popup is a user gesture, which is the most reliable
 * way for Chrome to hand a custom protocol to the operating system. The popup
 * stays open, but Chrome may suppress its contents while the OS prompt is up.
 */
function launchProtocol(protocolUrl) {
  const anchor = document.createElement("a");
  anchor.href = protocolUrl;
  anchor.rel = "noopener noreferrer";
  anchor.hidden = true;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

function handleSubmit(event) {
  event.preventDefault();

  const url = parseHttpUrl(currentTabUrl);
  if (!url) {
    setStatus("This page cannot be sent to HyperDL.", "error");
    return;
  }

  const connections = CONNECTIONS.includes(elements.connections.value)
    ? elements.connections.value
    : DEFAULT_SETTINGS.connections;

  launchProtocol(buildProtocolUrl(currentTabUrl, connections, confirmInApp));
  setStatus(
    "Opening HyperDL... If nothing happens, make sure HyperDL is installed and the hyperdl:// protocol is registered."
  );
}

async function initialize() {
  elements.version.textContent = `v${chrome.runtime.getManifest().version}`;
  populateConnections();
  elements.form.addEventListener("submit", handleSubmit);
  elements.options.addEventListener("click", () => {
    try {
      const result = chrome.runtime.openOptionsPage();
      if (result && typeof result.catch === "function") {
        result.catch((error) => {
          console.warn("HyperDL: could not open the options page.", error);
        });
      }
    } catch (error) {
      console.warn("HyperDL: could not open the options page.", error);
    }
  });

  const [tab, settings] = await Promise.all([getActiveTab(), getSettings()]);
  elements.connections.value = settings.connections;
  confirmInApp = settings.confirmInApp;

  if (!tab || typeof tab.url !== "string" || tab.url.length === 0) {
    elements.url.textContent =
      "This page's address is not available to the extension. Open a normal http:// or https:// page and try again.";
    showNotice(
      "HyperDL can only receive http:// and https:// links. This page cannot be sent.",
      "error"
    );
    return;
  }

  currentTabUrl = tab.url;
  elements.url.textContent = tab.url;
  elements.url.title = tab.url;

  if (!parseHttpUrl(currentTabUrl)) {
    showNotice(
      "HyperDL can only receive http:// and https:// links. This page cannot be sent.",
      "error"
    );
    return;
  }

  elements.send.disabled = false;

  if (!isDirectFileUrl(currentTabUrl)) {
    showNotice(
      "This looks like a web page, not a direct file link. HyperDL only supports direct HTTP/HTTPS file links. You can still send it, but the download may fail.",
      "warning"
    );
  } else {
    hideNotice();
  }
}

void initialize();
