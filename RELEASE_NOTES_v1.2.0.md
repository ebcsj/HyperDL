# HyperDL v1.2.0 Release Notes

## HyperDL v1.2.0

A usability update for the Windows x64 download manager: HyperDL now lives in the system tray, notifies you when downloads finish or fail, can watch the clipboard for download URLs (opt-in), and can offer to shut Windows down when the queue finishes. This release also cleans up code quality and more than doubles the coverage of the core components with new automated tests.

### Features

- System tray integration: closing or minimizing HyperDL hides it to the notification tray (enabled by default). Double-click the tray icon to restore the window; the tray menu offers open, pause all, resume all, and a real exit.
- Download notifications: completed and failed downloads raise a notification (enabled by default, configurable in Settings).
- Clipboard URL monitoring (opt-in, off by default): when enabled, copying a direct HTTP/HTTPS URL fills the URL box, where Enter adds the download.
- New "Paste" button next to the URL box fills it from the clipboard on demand.
- Shutdown prompt: when "Offer to shut down Windows when the queue finishes" is enabled and every queued download has finished, HyperDL asks for confirmation before shutting Windows down (cancellable with `shutdown /a`).
- Application icon for the executable, tray, and shortcuts.
- Everything from v1.1.x: concurrent segmented downloads, cooperative dynamic segmentation, buffer modes, retry with backoff, pause/resume with SQLite recovery, bilingual UI, Chrome extension bridge, `hyperdl://` protocol, and single-instance IPC.

### Improvements

- The global connection limiter now waits event-driven when the limit is reached instead of polling every 10 ms.
- Six unused localization strings were removed; the English and Traditional Chinese tables remain fully in parity (verified by tests).
- Latest .NET analyzers are enabled during builds with zero warnings.
- The test suite grew from 115 to 154 passing tests, adding direct coverage for the segment scheduler (split correctness, minimum tail, caps, coverage validation), bandwidth limiter (aggregate rate, reset), disk space guard (FAT32 limits, reserve math), and the download manager (queue, duplicate detection, pause/resume offsets, removal, concurrency limits).

### Documentation

- README, user guide, and troubleshooting (English and Traditional Chinese) document the new tray, notification, clipboard, and shutdown behavior.

### Download and install

1. Download `HyperDL_1.2.0_Setup.exe` from the assets below.
2. Run the installer and follow the setup instructions.
3. To update an existing installation, run the new Setup.

The installer is not code-signed, so Windows SmartScreen may display a warning. Download it only from this GitHub Release. `SHA256SUMS.txt` lists the checksums for the installer and the extension package; verify before running.

### Limitations

- Windows x64 only; accepts direct HTTP/HTTPS file URLs.
- FTP/FTPS, BitTorrent, and automatic updates are not supported.
- Pause and resume are available only for eligible segmented downloads.
- Clipboard monitoring and the shutdown prompt are off by default; enable them in Settings.
- Some servers, long-running transfers, and installation scenarios have not been verified.

---

## 繁體中文版

HyperDL v1.2.0 易用性更新：HyperDL 現在會進駐系統匣、在下載完成或失敗時通知你、可選擇監看剪貼簿中的下載網址（opt-in），並可在佇列完成時詢問是否關機。本次同時清理程式碼品質，並為核心元件新增大量自動化測試。

### 功能

- 系統匣整合：關閉或最小化 HyperDL 會隱藏到通知區（預設開啟）。雙擊系統匣圖示可還原視窗；系統匣右鍵選單提供開啟、全部暫停、全部繼續與結束。
- 下載通知：下載完成或失敗時顯示通知（預設開啟，可於設定關閉）。
- 剪貼簿網址監看（opt-in，預設關閉）：啟用後複製的直接 HTTP/HTTPS 網址會自動填入網址框，按 Enter 即可加入。
- 網址框旁新增「貼上」按鈕，可從剪貼簿手動填入。
- 關機提示：開啟「佇列完成時詢問是否關閉 Windows」且所有佇列下載都完成時，HyperDL 會先詢問確認才關機（可用 `shutdown /a` 取消）。
- 應用程式圖示（執行檔、系統匣與捷徑）。
- 完整保留 v1.1.x 功能：並行分段下載、合作式動態分段、緩衝區模式、退避重試、暫停／續傳與 SQLite 恢復、雙語介面、Chrome 擴充功能橋接、`hyperdl://` 協定與單一實例 IPC。

### 改進

- 全域連線限制器在達到上限時改為事件驅動等待，不再每 10 ms 輪詢。
- 移除六個未使用的本地化字串；英文與繁體中文字表保持完全對稱（由測試把關）。
- 建置啟用最新 .NET analyzers，維持 0 警告。
- 測試套件從 115 成長到 154 項全數通過，新增涵蓋：分段排程器（切分正確性、最小尾端、上限、覆蓋驗證）、頻寬限制器（聚合限速、重設）、磁碟空間防護（FAT32 限制、保留計算），以及下載管理器（佇列、重複偵測、暫停／續傳 offset、移除、併發上限）。

### 文件

- README、使用指南與疑難排解（英文與繁體中文）均已補上系統匣、通知、剪貼簿與關機功能的說明。

### 下載與安裝

1. 從下方附件下載 `HyperDL_1.2.0_Setup.exe`。
2. 執行安裝程式並依照畫面指示操作。
3. 若要更新既有安裝，請執行新版 Setup。

安裝程式尚未簽署，Windows SmartScreen 可能會顯示警告。請只從此 GitHub Release 下載。`SHA256SUMS.txt` 收錄安裝程式與擴充功能套件的檢查碼，執行前請先核對。

### 限制

- 僅支援 Windows x64 及直接 HTTP/HTTPS 檔案網址。
- 不支援 FTP/FTPS、BitTorrent 及自動更新。
- 暫停及繼續僅適用於符合條件的分段下載。
- 剪貼簿監看與關機提示預設關閉，請於設定中啟用。
- 部分伺服器、長時間下載及實際安裝情境尚未完成驗證。

---

## Chrome Extension 擴充功能

Chrome extension version: **1.2.0** (`HyperDL-Chrome.zip`).

- Please read the CHROME_EXTENSION.md file. This guide explains how to download and load the HyperDL Chrome extension for browser integration with the HyperDL app: https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.md

- 請閱讀 CHROME_EXTENSION.zh-TW.md 文件，本指南說明如何下載並載入 HyperDL Chrome 擴充功能，以便透過瀏覽器將下載連結傳送至 HyperDL 應用程式：https://github.com/ebcsj/HyperDL/blob/main/CHROME_EXTENSION.zh-TW.md

> Note / 備註：the current app release reads the `url` parameter and applies its own connection and confirmation settings; the extension's `connections` and `confirm` parameters are reserved for a future app update.／目前版本的應用程式只讀取 `url` 參數，並使用自身的連線數與確認設定；擴充功能的 `connections` 與 `confirm` 參數為未來更新預留。
