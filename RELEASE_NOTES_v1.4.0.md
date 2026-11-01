# HyperDL v1.4.0 Release Notes

## HyperDL v1.4.0

A convenience update for the Windows x64 download manager: HyperDL now keeps itself up to date (download + checksum verification + one-click install), answers command-line queries with JSON, integrates with Chrome through native messaging (no more external-protocol prompts), ships a dark theme, and lets you move settings between machines and reorder the queue by drag-and-drop.

### Features

- Automatic updates: when a newer release is published, HyperDL downloads the installer, verifies its SHA-256 against the release checksums, and asks before running it (enabled by default, opt-out in Settings; never installs without confirmation).
- CLI commands with JSON responses: `HyperDL.exe --list` prints the queue; `--pause-all`, `--resume-all`, and `--clear-completed` control the running instance; `--import <file>` batch-adds URLs (one per line, also usable as a cold start).
- Dark theme: switch in Settings, applied immediately and persisted.
- Settings import/export: move your configuration between machines as JSON.
- Queue drag-and-drop reordering in the download list.
- Native messaging: the Chrome extension hands links to HyperDL through Chrome's native messaging channel — no external-protocol prompt — falling back to the `hyperdl://` protocol automatically when the host is not registered.
- Everything from v1.3.x: SHA-256 verification, scheduled downloads, per-segment progress bars, system tray, notifications, clipboard monitoring, dynamic segmentation, buffer modes, bilingual UI.

### Improvements

- Fixed a v1.2 regression where a paused job could stay in "Pausing" (a stale-run guard compared boxed cancellation tokens, which never matched).
- The Chrome extension documents the honored parameter contract and shows its extension ID for native-messaging setup.
- Test suite grew from 184 to 199 passing tests (IPC protocol, native-messaging framing, settings store round trips).

### Documentation

- README, user guide, troubleshooting, and roadmap (English and Traditional Chinese) cover the CLI, native messaging, dark theme, settings transfer, and update flow; CHANGELOG.md now tracks every release.

### Download and install

1. Download `HyperDL_1.4.0_Setup.exe` from the assets below.
2. Run the installer and follow the setup instructions.
3. To update an existing installation, run the new Setup — or let HyperDL download it for you and confirm the install.

The installer is not code-signed, so Windows SmartScreen may display a warning. Download it only from this GitHub Release. `SHA256SUMS.txt` lists the checksums for the installer and the extension package; verify before running. Auto-downloaded installers are verified against the same checksums before HyperDL offers to run them.

### Limitations

- Windows x64 only; accepts direct HTTP/HTTPS file URLs.
- FTP/FTPS, BitTorrent are not supported; updates come only from this GitHub repository and are never installed without confirmation.
- Pause and resume are available only for eligible segmented downloads.
- Native messaging requires the extension ID to be pasted into Settings (unpacked extensions have per-machine IDs).
- Some servers, long-running transfers, and installation scenarios have not been verified.

---

## 繁體中文版

HyperDL v1.4.0 便利性更新：HyperDL 現在會自行保持最新——偵測新版本後下載安裝程式、以 SHA-256 驗證並詢問後安裝；支援命令列 JSON 查詢與控制；與 Chrome 以原生訊息整合（不再出現外部協定確認框）；新增深色主題、設定搬移與佇列拖曳排序。

### 功能

- 自動更新：發布新版本時自動下載安裝程式、以 Release checksums 驗證 SHA-256，並在執行前詢問（預設開啟，可於設定關閉；未經確認絕不安裝）。
- CLI 指令與 JSON 回覆：`HyperDL.exe --list` 輸出下載佇列；`--pause-all`、`--resume-all`、`--clear-completed` 控制執行中的執行個體；`--import <file>` 逐行批次加入網址（亦可作為冷啟動）。
- 深色主題：設定中切換，立即套用並保存。
- 設定匯入／匯出：以 JSON 在不同電腦間搬移設定。
- 佇列拖曳排序：在下載清單拖曳列即可調整順序。
- 原生訊息：Chrome 擴充功能優先透過原生訊息通道交付連結——不再出現外部協定確認框；主機未註冊時自動退回 `hyperdl://` 協定。
- 完整保留 v1.3.x 功能：SHA-256 驗證、排程下載、分段進度條、系統匣、通知、剪貼簿監看、動態分段、緩衝區模式、雙語介面。

### 改進

- 修正 v1.2 回歸：暫停的工作可能停留在「暫停中」（舊執行守衛以裝箱 token 比較，永遠不符）。
- Chrome 擴充功能文件記載參數合約，選項頁顯示擴充功能 ID 供原生訊息設定。
- 測試套件從 184 成長到 199 項全數通過（IPC 協定、原生訊息框、設定存放往返）。

### 文件

- README、使用指南、疑難排解與路線圖（英文與繁體中文）涵蓋 CLI、原生訊息、深色主題、設定搬移與更新流程；新增 CHANGELOG.md 記錄每個版本。

### 下載與安裝

1. 從下方附件下載 `HyperDL_1.4.0_Setup.exe`。
2. 執行安裝程式並依照畫面指示操作。
3. 若要更新既有安裝，請執行新版 Setup——或讓 HyperDL 自動下載後確認安裝。

安裝程式尚未簽署，Windows SmartScreen 可能會顯示警告。請只從此 GitHub Release 下載。`SHA256SUMS.txt` 收錄安裝程式與擴充功能套件的檢查碼，執行前請先核對。自動下載的安裝程式會以相同 checksums 驗證後才會詢問執行。

### 限制

- 僅支援 Windows x64 及直接 HTTP/HTTPS 檔案網址。
- 不支援 FTP/FTPS 與 BitTorrent；更新僅來自此 GitHub 儲存庫，且未經確認絕不安裝。
- 暫停及繼續僅適用於符合條件的分段下載。
- 原生訊息需將擴充功能 ID 貼入設定（未封裝擴充功能的 ID 因機器而異）。
- 部分伺服器、長時間下載及實際安裝情境尚未完成驗證。

---

## Chrome Extension 擴充功能

Chrome extension version: **1.4.0** (`HyperDL-Chrome.zip`).

- Please read the CHROME_EXTENSION.md file. This guide explains how to download and load the HyperDL Chrome extension for browser integration with the HyperDL app: https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.md

- 請閱讀 CHROME_EXTENSION.zh-TW.md 文件，本指南說明如何下載並載入 HyperDL Chrome 擴充功能，以便透過瀏覽器將下載連結傳送至 HyperDL 應用程式：https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.zh-TW.md
