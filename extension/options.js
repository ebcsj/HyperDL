"use strict";

const CONNECTIONS = ["auto", "1", "2", "4", "8", "16", "32", "64"];
const DEFAULT_SETTINGS = Object.freeze({
  connections: "auto",
  confirmInApp: true,
});

const elements = {
  form: document.getElementById("settings-form"),
  connections: document.getElementById("connections"),
  confirm: document.getElementById("confirm"),
  save: document.getElementById("save"),
  reset: document.getElementById("reset"),
  status: document.getElementById("status"),
  version: document.getElementById("version"),
};

function populateConnections() {
  for (const value of CONNECTIONS) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value === "auto" ? "Auto" : value;
    elements.connections.appendChild(option);
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

function applySettings(settings) {
  elements.connections.value = settings.connections;
  elements.confirm.checked = settings.confirmInApp;
}

function setStatus(message, kind) {
  elements.status.textContent = message;
  elements.status.className = kind ? `status ${kind}` : "status";
}

function setBusy(isBusy) {
  elements.save.disabled = isBusy;
  elements.reset.disabled = isBusy;
}

async function saveSettings(settings) {
  setBusy(true);
  try {
    await chrome.storage.sync.set(settings);
    setStatus("Settings saved. New downloads will use these defaults.", "success");
    return true;
  } catch (error) {
    console.error("HyperDL: could not save settings.", error);
    setStatus("Could not save settings. Chrome sync storage may be unavailable.", "error");
    return false;
  } finally {
    setBusy(false);
  }
}

function readForm() {
  return {
    connections: CONNECTIONS.includes(elements.connections.value)
      ? elements.connections.value
      : DEFAULT_SETTINGS.connections,
    confirmInApp: elements.confirm.checked,
  };
}

async function initialize() {
  elements.version.textContent = `Version ${chrome.runtime.getManifest().version}`;
  populateConnections();

  let settings = { ...DEFAULT_SETTINGS };
  try {
    const stored = await chrome.storage.sync.get(DEFAULT_SETTINGS);
    settings = normalizeSettings(stored);
  } catch (error) {
    console.warn("HyperDL: could not read settings, showing defaults.", error);
    setStatus("Could not read saved settings. Defaults are shown.", "error");
  }
  applySettings(settings);

  elements.form.addEventListener("submit", (event) => {
    event.preventDefault();
    void saveSettings(readForm());
  });

  elements.reset.addEventListener("click", () => {
    applySettings(DEFAULT_SETTINGS);
    void saveSettings({ ...DEFAULT_SETTINGS });
  });
}

void initialize();
