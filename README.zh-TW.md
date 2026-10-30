# HyperDL

> 一款適用於 Windows 的 HTTP/HTTPS 下載管理器；伺服器支援時採用分段下載，並以串流方式將資料寫入磁碟。

[繁體中文](README.zh-TW.md) | [English](README.md)

[使用指南](docs/user-guides/USER_GUIDE.md) ·
[疑難排解](docs/user-guides/TROUBLESHOOTING.md) ·
[專案結構](docs/PROJECT_STRUCTURE.zh-TW.md) ·
[文件索引](docs/README.zh-TW.md)

![Windows](https://img.shields.io/badge/Platform-Windows-0078D4)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-6C2EB9)
![Windows x64](https://img.shields.io/badge/Target-win--x64-0078D4)
![MIT License](https://img.shields.io/badge/License-MIT-green)

## 專案簡介

HyperDL 是以 C#、.NET 8 和 WPF 開發的 Windows 桌面下載管理器，供使用者下載
直接 HTTP 或 HTTPS 檔案網址。當伺服器確認支援 HTTP byte range，且提供可用的
檔案大小時，HyperDL 可將固定範圍分段並行下載；否則會改用單一連線。

網路資料會透過有界緩衝區串流寫入磁碟，不會先將完整檔案載入記憶體。符合條件的
分段下載會使用 `.part` 暫存檔及 SQLite metadata，以支援暫停、繼續及程式意外關閉
後的恢復。

HyperDL 是由 **Kerby Chow** 建立的獨立開源專案。

## 主要功能

- WPF 下載清單，顯示工作狀態、進度、速度、ETA 及分段資訊。
- 支援直接 HTTP/HTTPS 網址；工作開始前會驗證網址 scheme。
- 探測 HTTP Range，並驗證 `206 Partial Content`、`Content-Range` 及回應長度。
- 條件符合時使用固定範圍並行下載；否則回退至單連線串流下載。
- 可選 Auto 或固定連線數：1、2、4、8、16、32、64。實際分段數取決於檔案大小、
  伺服器支援及全域連線上限。
- 符合條件的分段下載支援暫停、繼續、取消及程式意外關閉後恢復。
- 使用有界緩衝區串流寫入；分段資料透過 `RandomAccess` 寫入同一 `.part` 檔案的
  指定 offset。
- 使用 SQLite 保存下載及分段 metadata；下載檔案內容寫入磁碟，不存放於資料庫。
- 檔名清理及避免覆蓋同名檔案。
- Low RAM 緩衝區模式及共用連線限制器。
- Windows x64 self-contained 發布及 per-user Inno Setup 安裝器。

## 專案狀態

| 功能 | 狀態 | 說明 |
|---|---|---|
| HTTP/HTTPS 下載 | 已實作 | 僅支援直接檔案網址。 |
| HTTP Range | 已實作 | 會驗證 Range 支援及 Partial Content 回應。 |
| 分段下載 | 已實作 | 支援固定分段與動態分段（合作式切分）。 |
| 單連線回退 | 已實作 | 無法安全分段時使用。 |
| 暫停／繼續 | 有條件支援 | 僅適用於合資格的分段工作，不適用於單連線回退。 |
| SQLite 恢復 | 有條件支援 | 保存分段工作的 metadata 及 offset，支援待處理佇列與自動續傳。 |
| 緩衝區模式 | 已實作 | `Auto` / `LowRam` / `Balanced` / `Fast`，全域 64 MiB 預算。 |
| 重試與逾時 | 已實作 | 指數退避、閒置逾時、`If-Range`、來源變更檢查。 |
| 磁碟檢查 | 已實作 | 磁碟空間預檢、FAT32 4 GB 限制。 |
| 頻寬限制與並行控制 | 已實作 | 速度上限、每主機/全域連線限制、並行下載數限制。 |
| 雙語介面 | 已實作 | 繁體中文／英文，可在設定切換。 |
| 瀏覽器整合 | 已實作 | `hyperdl://` 自訂協定、Chrome 擴充功能橋接、單一實例 IPC。 |
| 多選／右鍵選單／搜尋 | 已實作 | 多選操作、右鍵選單、搜尋篩選、清除已完成、編輯網址、刪除檔案、開啟記錄。 |
| FTP/FTPS | 不支援 | 未實作 FTP 用戶端。 |
| BitTorrent／磁力連結 | 不支援 | 未實作 Torrent 引擎。 |
| 自動更新 | 不支援 | 沒有更新器或更新來源。 |
| 公開 GitHub Release | 已上傳 | 請下載新版或執行新版 Setup 更新。 |

更多協定及安全說明請參閱[參考文件](docs/reference/README.zh-TW.md)及
[架構說明](docs/ARCHITECTURE.zh-TW.md)。

## 記憶體設計

下載資料會以串流方式逐步寫入磁碟：

```text
網路串流 → 有界 Worker 緩衝區 → 磁碟檔案
```

HyperDL 不會將完整檔案或完整分段載入記憶體，也不會以大型 `MemoryStream` 暫存整個下載。
緩衝區大小依據所選的緩衝區模式（`Auto`、`LowRam`、`Balanced`、`Fast`）動態調整，並受全域
64 MiB 網路緩衝預算限制。總記憶體使用量仍會受 Worker 數量、系統及其他執行程式影響，因此不承諾固定的程序記憶體上限。

## 連線模式

可選 Auto、1、2、4、8、16 或 32/64 條連線。Auto 依已知檔案大小選擇目標值：

| 檔案大小 | Auto 目標連線數 |
|---|---:|
| 小於 8 MiB | 1 |
| 8 MiB 至小於 32 MiB | 2 |
| 32 MiB 至小於 128 MiB | 4 |
| 128 MiB 至小於 1 GiB | 8 |
| 1 GiB 至小於 8 GiB | 16 |
| 8 GiB 至小於 32 GiB | 32 |
| 32 GiB 或以上 | 64 |

Auto 數值只是目標，不是保證。必須確認伺服器支援 Range、檔案大小已知，而且每個分段至少
符合 4 MiB 的 `MinimumSegmentSize`；否則 HyperDL 會減少分段數或改用單一連線。日常使用
建議選 Auto。較多連線可能觸發伺服器限速、HTTP 429 或降低速度。

## 異常關閉後恢復

分段工作會將原始及最終網址、路徑、檔案大小、狀態、來源驗證資訊及分段 offset 保存在
`%LOCALAPPDATA%\HyperDL\hyperdl.db`。工作執行中約每五秒儲存檢查點，並在狀態改變時更新。
程式重新啟動後不會自動開始下載；請在清單選取標示為可恢復的工作，再按 `繼續`。

繼續前 HyperDL 會重新探測來源，並比較可取得的檔案大小、ETag、Last-Modified 及 Range
支援資訊，以降低拼接不同檔案版本的風險。若無法確認相容性，會停止續傳。單連線回退工作
不會保存可恢復的下載工作階段。

## 系統需求

### 執行已發布版本

- Windows x64。
- 從可信發布來源取得的 HyperDL Setup 安裝程式。

發布版本為 .NET self-contained，設計上不需要使用者另外安裝 .NET Runtime。
目前 Setup 尚未簽署，Windows SmartScreen 可能會顯示警告。

### 從原始碼建置

- Windows 10 或 Windows 11 x64。
- .NET 8 SDK x64。
- PowerShell。
- 只有在需要編譯安裝程式時才需要 Inno Setup 6。

## 安裝

請從最新的 GitHub Release 下載 `HyperDL_Setup.exe` 並執行。若已安裝舊版，執行新版 Setup
即可更新。預設安裝路徑為：

```text
%LOCALAPPDATA%\Programs\HyperDL
```

Setup 安裝包已包含 self-contained 發布檔案；安裝時不會再從其他伺服器下載應用程式檔案。
一般解除安裝會移除已安裝的應用程式，但不會刻意刪除使用者下載內容或
`%LOCALAPPDATA%\HyperDL` 下的資料。

目前沒有提供 Portable ZIP；`scripts/publish.ps1` 會產生 self-contained 發布資料夾，但不會
將它封裝成 ZIP。安裝時請使用可信來源提供的 Setup。

## 快速開始

### 從原始碼執行

在 HyperDL 專案根目錄（包含 `HyperDL.sln` 和 `HyperDL.csproj` 的資料夾）開啟 PowerShell，
然後執行以下命令：

```powershell
dotnet restore HyperDL.sln
dotnet run --project HyperDL.csproj
```

### 下載檔案

1. 在 `URL` 欄輸入直接 HTTP 或 HTTPS 檔案網址。
2. 選擇 `Auto` 或連線數。
3. 按 `Start Download`。
4. 在視窗中查看進度、已下載大小、速度及狀態。
5. 選取完成的工作，再按 `開啟下載資料夾` 前往檔案位置。

預設下載目錄為目前 Windows 使用者的 `Downloads` 資料夾。有伺服器提供檔名時會優先採用；
否則使用網址路徑，必要時使用安全的 fallback 檔名。若目標檔名已存在，會加上序號以避免覆蓋。

完整的暫停／繼續行為及設定說明請參閱[使用指南](docs/user-guides/USER_GUIDE.md)。

## 使用說明

- **Auto 連線數：** 根據已知檔案大小選擇目標值。最終分段數可能因最小分段大小及
  全域連線上限而降低。增加連線數不保證速度較快。
- **暫停與繼續：** 僅適用於已確認 Range 支援且檔案大小已知的分段下載。繼續前會重新檢查來源。
- **單連線回退：** 在伺服器未提供安全分段所需資訊時仍可串流下載，但不支援暫停或繼續。
- **檔案大小未知：** 伺服器未提供 `Content-Length` 時仍可下載，但百分比及 ETA 可能無法顯示。
- **取消：** 停止目前下載。不同下載模式對未完成資料的處理方式不同，詳見
  [使用指南](docs/user-guides/USER_GUIDE.md)。
- **下載的程式：** HyperDL 不會自動執行下載的檔案。開啟前請確認來源。

## 設定與本機資料

應用程式設定儲存於：

```text
%LOCALAPPDATA%\HyperDL\settings.json
```

SQLite 下載歷史及恢復 metadata 儲存於：

```text
%LOCALAPPDATA%\HyperDL\hyperdl.db
```

設定包括下載目錄、偏好的連線數、最大同時下載設定、全域連線上限、Low RAM 模式及合法
使用確認。最大同時下載設定可儲存，但目前佇列工作仍會依序執行。

Low RAM 模式每個 Worker 使用 64 KiB 網路緩衝區；一般模式使用 80 KiB。這些是每個 Worker
的緩衝區大小，不代表整個處理程序的記憶體上限。SQLite 保存下載 metadata 及分段 offset，
不保存下載檔案內容。

HyperDL 執行中，或仍需要恢復下載時，請勿刪除資料庫、SQLite `-wal`／`-shm` 檔或 `.part`
檔案。一般解除安裝不會移除使用者資料。

## 隱私

HyperDL 將下載 metadata 和設定儲存在本機。資料庫可能包含下載網址、目的地路徑、檔名、
進度及來源驗證資訊；檔案內容則寫入下載目的地與 `.part` 暫存檔。

網址的路徑或查詢字串若含有私人資料，也可能被保存在本機資料庫，因此不要使用含密碼、
Token 或其他機密的網址。一般解除安裝會保留 `%LOCALAPPDATA%\HyperDL`、使用者下載檔及
安裝目錄以外的 `.part` 檔案。

## 解除安裝

使用 Windows 設定的「已安裝的應用程式」或開始功能表中的 `Uninstall HyperDL` 捷徑，
依照解除安裝程式指示操作。Inno Setup 會管理實際卸載程式的檔名，請勿假設檔名固定或
手動重新命名卸載程式。

一般解除安裝會移除程式安裝目錄、捷徑及 Windows 解除安裝登錄，但保留已下載檔案、
使用者資料夾中的 `.part`、設定、下載歷史及恢復 metadata。若要刪除本機設定及歷史，
請先解除安裝並關閉 HyperDL，再自行檢查及移除 `%LOCALAPPDATA%\HyperDL`。只有在確定
不再需要續傳時，才應手動刪除下載目錄中的 `.part` 檔案。

## 技術堆疊

| 項目 | 技術 |
|---|---|
| 程式語言 | C# |
| 桌面介面 | WPF／XAML |
| 目標 Framework | .NET 8（`net8.0-windows`） |
| 網路 | `HttpClient` |
| 檔案 I/O | 非同步串流及 `System.IO.RandomAccess` |
| 資料保存 | `Microsoft.Data.Sqlite` 8.0.11 |
| 測試 | xUnit 2.5.3、Microsoft.NET.Test.Sdk 17.8.0 |
| 發布 | .NET SDK、`win-x64`、self-contained |
| 安裝程式 | Inno Setup 6 |

## 開發與測試

在專案根目錄執行：

```powershell
dotnet restore HyperDL.sln
dotnet build HyperDL.sln -c Release
dotnet test HyperDL.sln -c Release --no-build
```

最近一次記錄的本機驗證（2026 年 10 月 27 日）結果為 **Build 0 錯誤、0 警告，115 項測試通過**。
這只是該次執行的結果，不代表其他 commit 或環境也一定有相同結果。

## 路線圖

- [x] M0：安全網、基線、日誌與 CI 基礎
- [x] M1：可靠性（重試、閒置逾時、If-Range、來源變更、緩衝模式、磁碟檢查、DB WAL/UPSERT、待處理佇列）
- [x] M2：動態分段（排程器、合作式切分、驗證、自適應）
- [x] M3：易用性與雙語（多下載、多選、搜尋篩選、右鍵選單、編輯網址、刪除、記錄）
- [x] M4：Chrome 擴充功能橋接、協定註冊、發佈/安裝器/CI 更新
- [ ] M5：系統匣/Toast/MVVM、HTTP/2 調校、SHA-256 校驗、排程、自動更新（延後）

建立 Windows x64 self-contained 發布目錄：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

此腳本會 Restore、Build、Test，並發布至 `artifacts\publish\HyperDL\`。腳本目前預期
.NET SDK 位於 `C:\Program Files\dotnet\dotnet.exe`。

建立 Setup 安裝程式（需要 Inno Setup 6）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

安裝程式會輸出至 `installer\Output\HyperDL_Setup.exe`。如需執行唯讀的安裝器靜態檢查：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-installer.ps1
```

人工安裝及解除安裝檢查清單位於
[安裝與解除安裝測試清單](docs/testing/INSTALL_UNINSTALL_TEST_CHECKLIST.zh-TW.md)。
清單項目尚未全部在乾淨的 Windows 電腦上驗證。

## 設定選項

介面設定儲存於 `%LOCALAPPDATA%\HyperDL\settings.json`；目前沒有必要設定的環境變數。
可在應用程式的 `設定` 面板調整：

| 設定 | 行為 |
|---|---|
| 下載目錄 | 檔案目的地；預設為目前使用者的 `Downloads`。 |
| 連線數 | Auto 或 1、2、4、8、16、32、64。 |
| 全域連線上限 | 活躍網路 Workers 共用的連線上限；範圍為 1–64。 |
| Low RAM 模式 | 使用 64 KiB 網路緩衝區，而非 80 KiB。 |
| 最大同時下載數 | 可保存的設定；目前佇列仍依序執行工作。 |

## 發布與 GitHub Release

提供給使用者的發布檔案是產生的 `installer\Output\HyperDL_Setup.exe`。可將該安裝程式
作為 GitHub Release 附件；原始碼 Repository 應保留發布腳本及 `.iss` 定義，不應提交產生
的 `bin/`、`obj/` 或 `artifacts/` 目錄。

最新版本已上傳，請下載新版或執行新版 Setup 更新既有安裝。

## 疑難排解

- **網址遭拒：** 請輸入直接、完整的 HTTP 或 HTTPS 檔案網址。不支援 FTP、BitTorrent、
  磁力連結或需要瀏覽器登入的流程。
- **沒有百分比：** 伺服器可能沒有提供 `Content-Length`；下載仍可在總大小未知時進行。
- **無法暫停：** 暫停／繼續需要確認 byte ranges 可用且檔案大小已知。
- **找不到 .NET：** 安裝 .NET 8 SDK x64。發布腳本也會檢查其設定的 SDK 路徑。
- **安裝器建置找不到 ISCC：** 安裝 Inno Setup 6。
- **Windows 對 Setup 顯示警告：** 目前的 Setup 未簽署。只執行從可信來源取得的檔案。

完整說明請參閱[疑難排解指南](docs/user-guides/TROUBLESHOOTING.md)。

## 已知限制

- 僅支援 Windows x64 及直接 HTTP/HTTPS 檔案網址。
- 不支援 Range 的伺服器會使用單連線；該模式不能暫停或續傳。
- 分段在下載開始前建立；不支援動態拆分或自適應連線。
- 更多連線不保證速度更快；伺服器可能限速、拒絕請求或降低吞吐量。
- 來源變更偵測能力取決於伺服器提供的檔案大小、ETag、Last-Modified 等資訊。
- 不支援 FTP/FTPS、BitTorrent、多工作排程器及自動更新。
- 未簽署的程式或安裝程式可能觸發 Windows SmartScreen。
- 目前沒有 Portable ZIP。

## 後續改善方向

以下為可能的改善項目，不代表已實作或承諾交付：

- [ ] 使用受控 HTTP 伺服器及大型檔案增加端對端測試。
- [ ] 改善下載清單、錯誤資訊及恢復流程。
- [ ] 評估下載排程與速度限制功能。
- [ ] 改善無障礙支援及介面語系。
- [ ] 改善簽署發布與安裝程式流程。

## 貢獻

歡迎貢獻。提交 Pull Request 前請先閱讀[貢獻指南](CONTRIBUTING.zh-TW.md)。程式碼變更應建置
Solution 並執行測試。請勿提交建置輸出、資料庫、`.part` 檔、憑證或私密金鑰。

安全問題請遵循 [安全性政策](SECURITY.zh-TW.md) 的指引。請勿在公開 Issue 中放入密碼、Cookie、Token、私人網址或個人檔案。

## 授權

HyperDL 採用 [MIT License](LICENSE)。著作權人為 **Kerby Chow**。
第三方套件聲明請參閱 [第三方套件聲明](THIRD_PARTY_NOTICES.zh-TW.md)。
