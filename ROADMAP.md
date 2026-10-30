# HyperDL 改造路線圖：速度、可靠性、易用性

> 狀態：**In Progress — 實作中**
> 更新日期：2026-10-27
> 適用版本：目前原始碼快照（已初始化 Git，baseline commit `4197598`）
> 備份：`C:\Users\user\Desktop\HyperDL-backup-20261026-230247`（已驗證，101 個檔案、52,542,457 bytes）

---

## 0. 已確認的產品決策

| 項目 | 決策 |
|---|---|
| 動態分段 | **要做**。列為核心功能，符合條件的下載預設開啟，設定可關閉。文件與產品定位同步更新。 |
| Buffer 模式 | **動態 + 使用者可選**。`Auto`、`Low RAM`（64 KiB）、`Balanced`（256 KiB）、`Fast`（1 MiB）。預設 `Auto`，維持 64 MiB 全域預算。 |
| 瀏覽器整合 | **做 Chrome 擴充功能（Manifest V3）**。先用 `hyperdl://` 自訂協定／CLI 作為橋樑；Native Messaging 之後再評估。 |
| 發佈方式 | **公開發佈到 GitHub**。CI 建置、Release 產物、Installer；簽章視證書取得情況；先做簡單的版本檢查。 |
| UI 語言 | **英文 + 繁體中文**。字串資源化，可切換；初期切換語言後重啟即可。 |
| 新套件 | **先不加** tray/MVVM 套件。系統匣、Toast、MVVM toolkit 之後再做；工作列進度用 WPF 內建 `TaskbarItemInfo`。 |
| 平台 | Windows x64（維持現況）。 |
| 非目標 | FTP/FTPS、BitTorrent、DRM／存取控制繞過、多來源鏡像下載。 |

---

## 1. 目標

1. **不再從 0 開始**：任何分段失敗、暫停、關窗或重開，都不應丟掉已完成資料。
2. **真正變快**：動態分段解決「最慢連線拖尾」，自適應連線數避免過度連線被限流。
3. **更好用**：佇列、多檔同時下載、Enter／拖放／批次貼上、CLI、Chrome 擴充功能。
4. **可公開發佈**：CI、Installer、Release 流程、英／繁中介面、錯誤訊息與文件。
5. **可維護**：先修核心，再做 UI；每個里程碑都有測試與量測。

### 非目標（本輪不做）

- 系統匣、Toast 通知、MVVM toolkit（延後）。
- Silent auto-update（先做「檢查新版本 + 引導下載」）。
- 多來源／鏡像下載、排程下載、FTP、BitTorrent。
- ARM64、Linux、macOS。

---

## 2. 目前的主要瓶頸

| 瓶頸 | 位置 | 影響 |
|---|---|---|
| 分段失敗就刪 `.part` | `Core/Downloading/DownloadEngine.cs:424-433`（單連線 `:287-295`） | 一次斷線就重新下載，大檔案最痛 |
| 關窗直接取消 | `MainWindow.xaml.cs:645-648` + `DownloadEngine.cs:414-423` | 進度與暫存檔消失，續傳形同無效 |
| 固定分段、無工作竊取 | `DownloadEngine.cs:340-362` | 最慢的那一段決定總完成時間 |
| Auto 分段保守、最小 4 MiB | `DownloadEngine.cs:56-67`、`:137-170` | 小檔案／高延遲連線無法受益 |
| 每讀一次就 `RandomAccess` 寫一次 | `DownloadEngine.cs:473-501` | syscall 多，HDD 隨機寫入嚴重 |
| 80 KiB buffer | `DownloadEngine.cs:15-16` | 高速鏈路吞吐受限 |
| 無重試、無 `Retry-After`、無閒置逾時 | `DownloadEngine.cs:461-470`、`:475-502` | 429/503／連線卡住就連鎖失敗 |
| DB 每次存檔都初始化 + 全刪全插 | `DownloadDatabase.cs:99`、`:153-177` | 每 5 秒重複 I/O；多下載時會互踩 |
| 一次只能一個下載 | `MainWindow.xaml.cs:94-97`；`MaximumConcurrentDownloads` 只存不用 `:585` | 佇列設定無效 |
| 進度 UI 每 250ms 重建分段文字 | `MainWindow.xaml.cs:252-259` | UI 負擔、容易卡頓 |
| 來源變更檢查有漏洞 | `DownloadSession.cs:104-119` | 伺服器停止送 ETag 時可能誤續傳 |
| `PartPath` 有存但載入時重算 | `DownloadDatabase.cs:207`、`:238-244` | 資料夾異動後可能找不到暫存檔 |

目前做得好的地方，改造時不要破壞：

- 嚴格的 `206 Partial Content`、`Content-Range`、長度驗證。
- 有界記憶體與 `.part` + SQLite checkpoint 設計。
- 續傳前重新探測來源。
- 64 連線上限與全域連線 limiter。

---

## 3. 改造原則

1. **先量測，再優化**：沒有 baseline 就不宣稱變快。
2. **可靠性就是速度**：避免重下載的收益，通常大於微調 buffer。
3. **不放寬安全驗證**：範圍、長度、ETag 檢查維持嚴格；相容性問題用單連線 fallback。
4. **記憶體有上限**：任何 buffer／分段策略都不得超過 64 MiB 網路緩衝預算。
5. **先引擎、後 UI**：先把下載核心與 DB 做穩，再做佇列與介面。
6. **每個里程碑都要有測試與量測報告**。

---

## 4. 里程碑

### M0 — 安全網與 baseline（0.5–1 天）

- [x] 備份目前專案（已完成並驗證）。
- [ ] `git init`、確認 `.gitignore`、建立 baseline commit。
- [ ] 安裝 .NET 8 SDK。
  - 目前 `C:\Program Files\dotnet\dotnet.exe` 存在，但找不到任何 SDK。
  - 優先使用官方 `dotnet-install.ps1` 安裝到使用者目錄，避免需要管理員權限；或使用 winget。
- [ ] 建立可重現的本機測試伺服器（range／限速／延遲／斷線／429／ETag 變更／長度不符）。
- [ ] 建立 baseline 量測表（見第 6 節）。
- [ ] 最小 logging（`%LOCALAPPDATA%\HyperDL\logs`）與診斷資訊。
- [ ] GitHub Actions：`build + test`。

**完成定義**：可以在本機重現建置、測試與 baseline 數據；任何後續改動都能對照。

---

### M1 — 可靠核心（3–5 天，最高 ROI）

#### M1.1 不再從 0 開始

- 每個分段獨立重試 3–5 次，指數退避 + jitter，支援 `Retry-After`。
- 只重試失敗區間；已完成分段不重下。
- 失敗、取消、關窗都**不刪 `.part`**；狀態改為 `Recoverable`。
- 關閉視窗改為 `Pause()`；啟動時載入可續傳工作，可設定自動續傳。
- `Cancel` 與「刪除檔案」分開：取消預設保留 `.part`，刪除需明確確認。

#### M1.2 連線不要卡死

- 加 per-read idle timeout（30–60 秒）；沒資料就重試。
- 下載用 `HttpClient.Timeout = InfiniteTimeSpan`，避免大分段／慢線路被整體逾時砍掉。
- 每個 range request 帶 `If-Range`（ETag / Last-Modified）。
- 收緊 `EnsureSourceUnchanged`：原本有 ETag／Last-Modified、現在沒有，視為來源變更。
- 續傳優先重新探測原始 URL（`OriginalUri`），失敗再退回 `FinalUri`，處理過期簽章連結。

#### M1.3 寫入管線與 Buffer 模式

- `ArrayPool<byte>` 降低配置。
- 每個 worker 使用 1 MiB 暫存緩衝，滿了才 `RandomAccess.WriteAsync`；**只有寫入成功才推進 offset**。
- `BufferMode`：`Auto` / `LowRam` / `Balanced` / `Fast`。
  - `LowRam`：64 KiB
  - `Balanced`：256 KiB
  - `Fast`：1 MiB
  - `Auto`：依可用記憶體與分段數在 128–512 KiB 間調整，必要時可升到 1 MiB
- 維持 `segments × buffer <= 64 MiB`。
- UI 加「Fast / Balanced / Low RAM / Auto」選擇，顯示預估記憶體用量。
- 保留舊設定 `LowRamMode` 的相容轉換。

#### M1.4 HTTP 調校

- 改用 `SocketsHttpHandler`：`ConnectTimeout=10s`、`PooledConnectionLifetime=5min`。
- Range request 明確送 `Accept-Encoding: identity`。
- 429/503/5xx：退避、降低該 host 併發、必要時暫停該 job 再恢復。
- HTTP/2 先做開關與 A/B 測試，不預設。

#### M1.5 資料庫與續傳

- `journal_mode=WAL`、`synchronous=NORMAL`、`busy_timeout`。
- `InitializeAsync` 只在啟動跑一次；`SaveSessionAsync` 不再呼叫初始化。
- 移除 `SaveSessionAsync` 內的 recovery UPDATE（避免把其他工作中標成 Recoverable）。
- Segments 改用 UPSERT，只寫有變動的資料。
- 加 `PRAGMA user_version` 與 migration。
- 載入時使用資料庫存的 `PartPath`。
- checkpoint 間隔可調整（預設 3–5 秒），分段完成時立即存一次。

#### M1.6 磁碟與錯誤

- 下載前檢查磁碟空間；FAT32 4 GB 警告。
- 錯誤分類：403／404／429／5xx／TLS／磁碟滿／來源變更／權限。
- 錯誤訊息附「重試」與「複製詳細資料」。

**完成定義**：

- 大檔案斷線後只重下失敗區段。
- 關窗重開後能續傳。
- 429/503 能自動恢復。
- Buffer 模式可切換且不超過記憶體預算。
- DB 在多工作情況下不互踩。

---

### M2 — 動態分段（4–6 天）

> 這是本輪的產品定位改變：文件目前寫「不支援動態分段」，完成後要改。

#### M2.1 排程器

- 新增 `SegmentScheduler`／工作佇列，取代目前「固定 Task 陣列」。
- 初始分段仍由 `RangeCalculator` 產生。
- Worker 從佇列取工作；完成後回報，調度器決定是否切分。

#### M2.2 合作式切分（cooperative split）

- 每個 worker 的區間允許被縮短：調度器把 `EndOffset` 往下調，尾端建立新分段。
- Worker 每輪檢查 `CurrentOffset` 是否超過新的 `EndOffset`；超過就停止、關閉 response。
- 只丟棄尚未寫入的尾端資料；新分段從切分點重新請求，保證不重疊、不遺漏。
- 只切「夠大」的區間：最小尾端 4 MiB，最多 128 個分段（或初始分段數 × 2，取較大者）。
- 優先切最大的未完成區間，避免碎片化。

#### M2.3 持久化與恢復

- 新分段寫入 `Segments` 表；保留 `SegmentIndex` 唯一性。
- 暫停／crash 後可載入動態分段並續傳。
- 完成時驗證：所有分段涵蓋 `[0, totalBytes)`、無重疊、總長度正確。
- 任何覆蓋錯誤都視為失敗，不重新命名 `.part`。

#### M2.4 自適應連線（先做簡單版）

- 前 3–5 秒量測每條連線吞吐。
- 若每條連線仍接近滿速，逐步增加 worker；若出現 429/503 或吞吐下降，降低。
- 每 host 連線上限；全域仍受 `GlobalConnectionLimiter` 控制。
- Auto 連線數保留，但可被動態調整。

#### M2.5 UI 與設定

- 設定加「動態分段」開關（Auto／On／Off）。
- 預設：檔案 ≥ 64 MiB 且支援 range 時開啟；小檔案維持固定分段。
- 顯示目前活動分段數與每段進度（避免每 250ms 重建整頁文字）。

#### M2.6 文件與測試

- 更新 `docs/ARCHITECTURE.md`、`docs/reference/PROTOCOLS.md`、`README.md` 的支援範圍。
- 測試：切分正確性、暫停／續傳、crash 恢復、重疊／缺口、最大分段數、小檔案、64 連線、切分中的錯誤。
- Benchmark：固定分段 vs 動態分段，使用「一條慢連線」的情境量測完成時間。

**完成定義**：

- 動態分段在異質連線測試中帶來可量測的完成時間改善。
- 任何情況下都不會產生缺口或重疊。
- 暫停、關窗、重開後仍可正確續傳。
- 文件不再宣稱「不支援動態分段」。

---

### M3 — 易用性與雙語（5–7 天）

#### M3.1 佇列與多下載

- 抽出 `DownloadManager`，尊重 `MaximumConcurrentDownloads`。
- 每個 job 自己的 CTS；全域連線數公平分配。
- 一個完成自動接續下一個；支援「全部暫停／全部繼續」。
- 每個項目：暫停、繼續、取消、重試、移除。

#### M3.2 加入下載的流程

- Enter 開始下載。
- 拖放 URL 或檔案到視窗。
- 批次貼上（一行一個 URL）。
- `HyperDL.exe --add <url>` 命令列支援。
- 單一實例 + named pipe 轉交 URL。
- `hyperdl://add?url=...` 自訂協定（Chrome 擴充功能的橋樑）。
- 剪貼簿監聽列為選配（opt-in），避免隱私爭議。

#### M3.3 完成後的反饋

- 雙擊開檔、右鍵「開啟檔案／開啟資料夾」。
- 工作列進度（WPF 內建）。
- 應用程式內通知；Toast／系統匣延後。
- 可選「完成後關機」（延後到 M5）。

#### M3.4 列表與設定

- 多選、右鍵選單、清除已完成、搜尋／篩選／排序。
- 記住視窗大小、位置、欄位寬度。
- 重試失敗項目、複製 URL、編輯 URL（處理過期連結）。
- 資料夾選擇器、速度上限、buffer 模式、動態分段、自動續傳、語言、每 host 上限、最大同時下載。

#### M3.5 雙語

- 所有 UI 字串資源化（ResourceDictionary 或 `.resx`）。
- 英文（中性）+ 繁體中文（zh-TW）。
- 語言選擇器；預設跟隨系統；初期切換後重啟。
- 錯誤訊息、狀態、對話框、文件同步翻譯。
- 加一個簡單的資源鍵檢查測試，避免漏翻。

#### M3.6 錯誤 UX

- 例外轉成可讀訊息與建議動作。
- 「重試」按鈕；「複製詳細資料」。
- 不再中英混雜。

**完成定義**：

- 可同時下載多個檔案，數量受設定控制。
- 可用 Enter／拖放／批次貼上／CLI／Chrome 橋樑加入下載。
- 英文與繁中都能完整操作。
- 錯誤訊息可理解、可重試。

---

### M4 — Chrome 擴充功能與公開發佈（4–6 天）

#### M4.1 整合橋樑

- 優先方案：`hyperdl://add?url=<encoded>` 自訂協定。
  - Installer 註冊 `HKCU\Software\Classes\hyperdl`。
  - App 收到 URL 後**先顯示確認對話框**，不自動開始（避免惡意網站濫用）。
  - 只接受 HTTP/HTTPS；拒絕其他 scheme。
- 備選方案：Native Messaging（較安全、安裝較複雜）；先不做，保留設計空間。
- CLI 與自訂協定共用同一個 `AddDownloadRequest` 處理流程。

#### M4.2 Chrome 擴充功能（Manifest V3）

- 右鍵連結：「Download with HyperDL」。
- 工具列 popup：目前分頁 URL、連線數、送出。
- 選項頁：預設連線數、是否確認、顯示版本。
- 權限最小化：`contextMenus`、`activeTab`、`storage`。
- 非直接檔案 URL（例如 YouTube 頁面）顯示警告，不假裝支援。
- 提供 unpacked 安裝說明；若上架 Chrome Web Store，补上商店文案與隱私政策。

#### M4.3 發佈流程

- GitHub Actions：
  - `build + test`
  - `publish win-x64 self-contained`
  - `build installer`（Inno Setup）
  - 上傳 Release artifacts
- `scripts/publish.ps1` 不再寫死 `C:\Program Files\dotnet\dotnet.exe`。
- `PublishReadyToRun=true` 改善啟動；評估 single-file。
- 版本檢查：啟動時可選檢查 GitHub Releases，顯示新版本與下載連結（不 silent install）。
- 簽章：有證書就簽 `.exe` 與 installer；沒有則在 README 說明 SmartScreen。
- 更新 README、使用者指南、疑難排解、CHANGELOG、SECURITY、Release checklist。

**完成定義**：

- 使用者可從 GitHub Release 下載 installer 並安裝。
- Chrome 擴充功能可把連結送到 HyperDL。
- CI 能產出可發佈的產物。
- 英文／繁中文件與 UI 一致。

---

### M5 — 延後項目

- 系統匣圖示、Toast 通知。
- MVVM toolkit／大規模 UI 重構。
- HTTP/2 預設、連線預熱。
- SHA-256 校驗。
- 排程下載、完成後關機。
- 多來源鏡像下載。
- Silent auto-update。
- ARM64。

---

## 5. 技術設計摘要

### 5.1 動態分段

- 以「區間」為工作單位，而不是固定 Task。
- 調度器持有所有分段；worker 完成後請求下一個工作。
- 切分只發生在「足夠大」的區間，且保留最小尾端 4 MiB。
- 以 atomic/Interlocked 方式縮短 `EndOffset`；worker 在每個 read 迴圈檢查。
- 任何切分都先寫入記憶體佇列，checkpoint 時持久化；恢復時以 DB 為準。
- 完成驗證：排序後檢查 `start == previousEnd + 1`，且最後一段等於 `totalBytes - 1`。

### 5.2 Buffer 模式

```
LowRam    64 KiB
Balanced  256 KiB
Fast      1 MiB
Auto      clamp(128 KiB .. 1 MiB, budget / segmentCount)
```

- 全域預算 64 MiB；超過時自動下調，不讓使用者設定弄壞記憶體上限。
- `Auto` 預設；UI 顯示「預估記憶體」與「目前模式」。
- 低記憶體機器或 64 分段時自動降級。

### 5.3 資料庫與續傳

- 啟動時只初始化一次；使用 WAL。
- `Segments` 以 `(DownloadId, SegmentIndex)` UPSERT。
- 以 `PRAGMA user_version` 管理 schema migration。
- 續傳前：重新探測、比對大小／ETag／Last-Modified；缺少原本有的驗證資訊視為變更。
- `.part` 只在使用者明確選擇「刪除檔案」時刪除。

### 5.4 佇列與連線分配

- `DownloadManager` 持有 `SemaphoreSlim(maxConcurrent)`。
- 每個 job 的目標連線數 = `min(jobSetting, globalLimit / activeJobs)`，避免 starvation。
- 動態分段的新 worker 也必須先取得全域 limiter 的 lease。
- 佇列狀態機：`Queued → Downloading → Paused / Recoverable / Completed / Failed / Cancelled`。

### 5.5 Chrome 擴充功能

- 第一版用自訂協定，不改動 Chrome 的下載行為。
- App 端一律顯示確認，避免網站惡意觸發大量下載。
- 只處理 HTTP/HTTPS 直接檔案；其他型態明確提示不支援。
- 擴充功能與 App 的介面保持單一：`hyperdl://add?url=...`。

### 5.6 雙語 UI

- 資源鍵以英文為中性語言，繁中為衛星資源。
- 預設跟隨 `CultureInfo.CurrentUICulture`；設定可覆寫。
- 初期切換語言需重啟；之後可改成即時切換。
- 文件同步提供 `.md` / `.zh-TW.md`。

### 5.7 發佈與更新

- GitHub Actions 產出 installer 與 portable zip。
- 版本檢查只提示，不下載、不自動安裝。
- 有簽章證書時再開啟簽章；沒有時文件說明 SmartScreen。

---

## 6. 測試與量測

### 6.1 測試矩陣

| 維度 | 案例 |
|---|---|
| 檔案大小 | 100 MB、1 GB、10 GB、< 8 MiB、0 byte |
| 連線數 | 1、8、16、32、64、Auto |
| 網路 | 本機、一般寬頻、100ms 延遲、1% 封包遺失 |
| 異常 | 5% 斷線、429 + Retry-After、503、不支援 range、ETag 變更、Content-Length 不符、416 |
| 操作 | 暫停、繼續、取消、關窗、crash、磁碟滿、資料夾唯讀 |
| 分段 | 固定、動態、切分中暫停、切分中斷線、最大分段數 |
| UI | 英／繁中、鍵盤操作、拖放、多選、批次貼上 |
| 整合 | CLI、`hyperdl://`、Chrome 擴充功能、installer |

### 6.2 量測指標

- 平均吞吐（MB/s）
- 完成時間（s）
- 重下載位元組數
- 失敗恢復成功率
- 峰值 working set
- CPU 使用率
- UI 更新延遲／卡頓

### 6.3 驗收門檻

- 大檔案斷線後只重下失敗區段。
- 關窗重開後能續傳。
- 動態分段在異質連線測試中優於固定分段（以 baseline 為準，不承諾固定百分比）。
- 2 個以上 job 同時跑，全域連線不超過設定。
- 429/503 能自動恢復。
- 峰值記憶體不超過 64 MiB 網路緩衝預算（不含 runtime/UI/SQLite）。
- 英文與繁中 UI 都能完成主要流程。
- CI 能從乾淨環境建置、測試、產出 installer。

---

## 7. 風險與取捨

| 風險 | 對策 |
|---|---|
| 更多連線不一定更快，可能被 CDN 限流 | 每 host 上限、429/503 退避、動態降連線 |
| 動態分段實作複雜，可能產生缺口／重疊 | 嚴格的覆蓋驗證、最小尾端、分段上限、完整測試 |
| 大 buffer 增加記憶體 | 64 MiB 預算、Auto 模式、Low RAM 選項 |
| 保留 `.part` 可能佔用磁碟 | 清楚的刪除選項、磁碟空間預檢、舊檔清理提示 |
| DB schema 變更 | `user_version` migration、先備份 DB、啟動時備份 |
| 雙語字串漏翻 | 資源鍵檢查測試、CI 檢查 |
| 自訂協定被惡意網站濫用 | App 端確認對話框、只接受 HTTP/HTTPS、速率限制 |
| 公開釋出的支援負擔 | 診斷 log、錯誤代碼、Issue template、CHANGELOG |
| 無簽章觸發 SmartScreen | 文件說明；有證書後補簽章 |
| UI 重構範圍過大 | 先引擎、後 UI；每個 PR 保持可建置 |

**安全底線**：不加入 DRM／TLS 繞過、不關閉憑證驗證、不放寬 range 驗證來換相容性。

---

## 8. 批准後的第一個 Sprint（1–3 天）

> 只有收到「開始」才會執行。

1. `git init` + baseline commit。
2. 安裝 .NET 8 SDK，確認 `dotnet build` / `dotnet test` 可跑。
3. 建立本機測試伺服器與 baseline 量測表。
4. M1.1：per-segment retry、保留 `.part`、關窗 Pause、自動續傳。
5. M1.2：idle timeout、`If-Range`、來源變更收緊。
6. M1.5：DB WAL、初始化一次、UPSERT、migration。
7. M1.3：Buffer 模式 + UI 選擇器。
8. M1.6：磁碟空間檢查與錯誤分類。
9. 補測試，產出第一份 before/after 報告。

**交付物**：可建置的 patch release candidate、測試結果、更新後的 `ROADMAP.md` 勾選狀態。

---

## 9. 工作流程與批准規則

- 本檔案是改造期間的 single source of truth。
- **在收到明確指示前，不改動應用程式原始碼。**
- 每個里程碑開始前確認範圍；結束時：
  - 全部測試通過
  - 更新本檔案
  - 提供簡短量測報告
  - 建立 git commit（每個里程碑至少一個）
- 風險較高的變更（DB migration、動態分段）先備份資料庫與專案。
- 每個 PR／commit 保持可建置；不把 UI 重構與引擎改造混在一起。

---

## 10. 待決事項

1. **.NET 8 SDK 安裝方式**：系統安裝（winget）或使用者目錄（dotnet-install.ps1）？
2. **Code signing**：有沒有 Windows code signing 證書？沒有就先發 unsigned，文件說明 SmartScreen。
3. **Chrome Web Store**：只提供 unpacked 安裝，還是要上架？（上架需要開發者帳號與隱私政策）
4. **GitHub repo**：公開位址與 owner 名稱，之後更新檢查與 Release 需要。
5. **Installer 顯示名稱／Publisher**：沿用 Kerby Chow / HyperDL？
6. **預設動態分段**：建議「≥ 64 MiB 且支援 range 時開啟」；是否同意？
7. **預設 Buffer 模式**：建議 `Auto`（約 256 KiB 起，視情況升到 1 MiB）；是否同意？
8. **最低 Windows 版本**：目前 .NET 8 + WPF，建議 Windows 10 1809 以上；是否同意？

---

## 11. 變更紀錄

| 日期 | 內容 |
|---|---|
| 2026-10-26 | 初版：加入動態分段、Buffer 模式、Chrome 擴充功能、雙語、公開發佈決策；建立備份。 |
