# HyperDL v1.3.0 Release Notes

## HyperDL v1.3.0

A trust-and-control update for the Windows x64 download manager: every finished download is hashed with SHA-256 and can be verified against the source's expected value, queued downloads can be scheduled for a future start time, per-segment progress bars show exactly how a segmented download is progressing, an optional HTTP/2 mode is available for measurement, and HyperDL can tell you when a newer release is published. The Chrome extension's `connections` and `confirm` parameters are now honored by the app.

### Features

- SHA-256 integrity: finished downloads are hashed automatically; right-click a job to `Verify SHA-256…` against an expected value from the source or `Copy SHA-256` for sharing.
- Scheduled downloads: right-click a queued job, choose `Schedule…`, and set a future start time (`yyyy-MM-dd HH:mm`); clearing the schedule starts the job immediately. Schedules survive restarts (database schema v3).
- Per-segment progress: the details panel shows a mini progress bar for every segment of a segmented download plus a completion summary.
- Extension parameters honored: `connections` overrides the per-download connection count for handed-off links, and `confirm=1` always shows the app confirmation dialog (the app's own confirm setting can never be bypassed by `confirm=0`).
- Startup update check (opt-out): HyperDL compares the latest GitHub release with its own version and shows a download link in the status bar; it never downloads or installs anything.
- Optional HTTP/2 negotiation (off by default; HTTP/1.1 remains the strict default).
- Everything from v1.2.x: system tray with minimize-to-tray, download notifications, opt-in clipboard monitoring, shutdown prompt, dynamic segmentation, buffer modes, retry with backoff, pause/resume with SQLite recovery, bilingual UI, and single-instance IPC.

### Improvements

- Fixed a v1.2 regression where a paused job could stay in the "Pausing" state: the manager now verifies that a cancellation belongs to the current run before touching job state.
- Fixed the same class of stale-run interference for failed jobs.
- Scheduled and per-job connection overrides persist through the pending-queue store (SQLite schema v3 with automatic migration).
- The Chrome extension README now documents the honored parameter contract.

### Documentation

- README, user guide, troubleshooting, architecture, and threat model (English and Traditional Chinese) cover hashing, scheduling, HTTP/2, segment bars, the update check, and the extension parameter semantics.

### Download and install

1. Download `HyperDL_1.3.0_Setup.exe` from the assets below.
2. Run the installer and follow the setup instructions.
3. To update an existing installation, run the new Setup.

The installer is not code-signed, so Windows SmartScreen may display a warning. Download it only from this GitHub Release. `SHA256SUMS.txt` lists the checksums for the installer and the extension package; verify before running.

### Limitations

- Windows x64 only; accepts direct HTTP/HTTPS file URLs.
- FTP/FTPS, BitTorrent, and automatic updates are not supported.
- Pause and resume are available only for eligible segmented downloads.
- HTTP/2 is experimental and off by default; scheduled times apply only to jobs that have not started downloading.
- Some servers, long-running transfers, and installation scenarios have not been verified.

---

## 繁體中文版

HyperDL v1.3.0 信任與控制更新：每個完成的下載都會以 SHA-256 計算雜湊，並可與來源提供的預期值驗證；佇列工作可排程未來開始時間；分段下載的每段進度一目瞭然；提供選用的 HTTP/2 模式供量測；當有新版本發布時 HyperDL 會主動提示。Chrome 擴充功能的 `connections` 與 `confirm` 參數現已由應用程式支援。

### 功能

- SHA-256 完整性：下載完成自動計算雜湊；右鍵工作選擇「驗證 SHA-256…」與來源預期值比對，或「複製 SHA-256」分享。
- 排程下載：右鍵尚未開始的工作選擇「排程…」設定未來開始時間（`yyyy-MM-dd HH:mm`）；清空排程立即開始。排程會跨重新啟動保存（資料庫 schema v3）。
- 分段進度：詳細資料區為分段下載的每一段顯示迷你進度條，並附完成摘要。
- 擴充功能參數支援：`connections` 覆寫該連結的下載連線數，`confirm=1` 一定顯示應用程式確認對話框（應用程式自身的確認設定不可能被 `confirm=0` 繞過）。
- 啟動更新檢查（可關閉）：比對最新 GitHub Release 與自身版本，於狀態列顯示下載連結；不會自動下載或安裝。
- 選用 HTTP/2 協商（預設關閉；預設仍為嚴格 HTTP/1.1）。
- 完整保留 v1.2.x 功能：系統匣與最小化到匣、下載通知、opt-in 剪貼簿監看、關機提示、動態分段、緩衝區模式、退避重試、暫停／續傳與 SQLite 恢復、雙語介面、單一實例 IPC。

### 改進

- 修正 v1.2 的一個回歸：暫停的工作可能停留在「暫停中」狀態——管理器現在會驗證取消事件屬於目前執行才會更新工作狀態。
- 修正失敗工作的同類舊執行干擾問題。
- 排程與每工作連線數覆寫會透過待處理佇列保存（SQLite schema v3 自動遷移）。
- Chrome 擴充功能 README 已記載正式的參數合約。

### 文件

- README、使用指南、疑難排解、架構與威脅模型（英文與繁體中文）均已涵蓋雜湊、排程、HTTP/2、分段進度條、更新檢查與擴充功能參數語意。

### 下載與安裝

1. 從下方附件下載 `HyperDL_1.3.0_Setup.exe`。
2. 執行安裝程式並依照畫面指示操作。
3. 若要更新既有安裝，請執行新版 Setup。

安裝程式尚未簽署，Windows SmartScreen 可能會顯示警告。請只從此 GitHub Release 下載。`SHA256SUMS.txt` 收錄安裝程式與擴充功能套件的檢查碼，執行前請先核對。

### 限制

- 僅支援 Windows x64 及直接 HTTP/HTTPS 檔案網址。
- 不支援 FTP/FTPS、BitTorrent 及自動更新。
- 暫停及繼續僅適用於符合條件的分段下載。
- HTTP/2 為實驗性且預設關閉；排程時間僅適用於尚未開始下載的工作。
- 部分伺服器、長時間下載及實際安裝情境尚未完成驗證。

---

## Chrome Extension 擴充功能

Chrome extension version: **1.3.0** (`HyperDL-Chrome.zip`).

- Please read the CHROME_EXTENSION.md file. This guide explains how to download and load the HyperDL Chrome extension for browser integration with the HyperDL app: https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.md

- 請閱讀 CHROME_EXTENSION.zh-TW.md 文件，本指南說明如何下載並載入 HyperDL Chrome 擴充功能，以便透過瀏覽器將下載連結傳送至 HyperDL 應用程式：https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.zh-TW.md
