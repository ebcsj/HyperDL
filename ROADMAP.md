# HyperDL 改進路線圖

> 狀態：v1.2.0 實作中
> 更新日期：2026-11-01
> 本檔案是改進期間的 single source of truth。在收到明確指示前，不改動應用程式原始碼。

---

## v1.2.0 — 本輪範圍

### Phase 0 — 發布基礎 ✅
- [x] v1.1.0→v1.1.1 的所有工作拆成結構化 commits（core / packaging / docs / release）
- [x] 隱私修復入庫：Setup 排除 PDB、csproj PathMap 移除本機路徑

### Phase 1 — 程式碼品質 ✅
- [x] 移除 6 個未使用的本地化 key（EN/ZH 字表保持對稱）
- [x] GlobalConnectionLimiter 超限等待改為事件驅動（移除 10ms 輪詢）
- [x] 啟用 latest .NET analyzers（建置維持 0 警告）

### Phase 2 — 測試覆蓋 ✅
- [x] SegmentScheduler：TryTake/TrySplit（對半切、最小尾端、上限、停用、寫入中保護）＋ Validate（缺口/未完成）
- [x] BandwidthLimiter：聚合限速、SetLimit 歸零、並發共用漏桶
- [x] DiskSpaceGuard：FAT32 4 GiB 邊界、reserve 計算、異常型別、負數輸入
- [x] DownloadManager：AddUrl 完成/持久化、活躍重複偵測、批次貼上去重、Remove 清記錄、暫停保留 offset→續傳完成、同時下載數=1 序列化

### Phase 3 — 通知與系統整合 ✅
- [x] 應用程式圖示（Assets/app.ico，與擴充功能同設計）
- [x] 系統匣圖示：最小化／關閉 → 隱藏到系統匣（`MinimizeToTray`，預設開）；雙擊還原；右鍵選單（開啟／全部暫停／全部繼續／結束）
- [x] 下載完成／失敗通知（系統匣氣泡；`ShowNotifications`，預設開）
- [x] 剪貼簿監聽（opt-in，`ClipboardMonitoring` 預設關）：偵測新支援 URL → 填入網址框 + 提示；新增「貼上」按鈕
- [x] 完成後關機（`ShutdownWhenComplete`，預設關）：佇列清空時確認對話框 → `shutdown /s /t 30`

### 發布 ✅
- [x] 版本 1.2.0（csproj / installer / extension manifest 對齊）
- [x] 重建 `HyperDL_Setup.exe`（installer\Output）與 `HyperDL-Chrome.zip`（artifacts\extension）
- [x] Release Notes v1.2.0（雙語）+ SHA256SUMS

---

## v1.3.0 — 本輪範圍

### 下載功能 ✅
- [x] SHA-256 校驗：完成後串流計算並顯示；右鍵驗證（貼入預期值比對）與複製
- [x] 排程下載：佇列工作的未來開始時間（PendingDownloads.StartAfter，schema v3）
- [x] HTTP/2 開關（`UseHttp2`，預設關閉維持 HTTP/1.1 exact）

### 整合 ✅
- [x] 擴充功能 `connections`/`confirm` 參數：per-job 連線數覆寫；confirm=1 補加確認、confirm=0 不繞過 App 設定
- [x] 版本檢查：啟動時查 GitHub Releases API（僅提示連結，不自動安裝）
- [x] 分段進度視覺化：詳細區每段迷你進度條 + 完成摘要

### v1.4.0 — 本輪範圍

### 發布與整合 ✅
- [x] GitHub 遠端建立並推送全部 commits 與 tags；Release workflow 由 tag 觸發
- [x] CHANGELOG.md（1.1.0 → 1.4.0）
- [x] CLI／IPC 雙向指令：`--list`、`--pause-all`、`--resume-all`、`--clear-completed`、`--import <file>`，JSON 回覆（named pipe + AttachConsole）
- [x] Native Messaging：stdio host（`--native-host`）、host manifest 註冊（擴充功能 ID 綁定）、擴充功能原生優先＋協定 fallback

### UI ✅
- [x] 深色主題：主題字典 + 執行時切換（`DarkMode` 設定）
- [x] 設定匯入／匯出（JSON）
- [x] 佇列拖曳排序

## 候選待辦（未承諾）
- [ ] Native Messaging（取代 hyperdl:// 協定橋接，免除 Chrome 外部協定確認框）
- [ ] CLI 擴充（--list、--pause-all、JSON 輸出）
- [ ] 自適應連線數精進（依每條連線吞吐動態增減）
- [ ] MVVM toolkit 重構（現為 code-behind）
- [ ] 深色模式、設定匯入／匯出、ARM64 build
- [ ] 多來源鏡像下載

### 清理
- [ ] 擴充功能 README 與 App 參數合約對齊（隨 `connections`/`confirm` 實作移除「參數保留」註記）

---

## 原則

1. 先量測再優化；可靠性就是速度。
2. 不放寬安全驗證：range／長度／ETag 檢查維持嚴格。
3. 記憶體上限：任何緩衝策略不得超過 64 MiB 網路緩衝預算。
4. 隱私：剪貼簿監聽預設關閉（opt-in）；關機功能預設關閉且執行前確認。
5. 安全底線：不加入 DRM／TLS 繞過、不關閉憑證驗證。
6. 每個版本：build 0 警告、全部測試通過、雙語文件同步、EN/ZH 字表對稱。

## 變更紀錄

| 日期 | 內容 |
|---|---|
| 2026-11-01 | 重建本檔：v1.2.0 範圍（品質清理、測試補強、通知與系統匣）與 v1.3.0 候選清單。 |
| 2026-11-01 | v1.3.0 範圍確定並實作：SHA-256 校驗、排程下載（DB v3）、HTTP/2 開關、擴充功能參數、分段視覺化、版本檢查。 |
