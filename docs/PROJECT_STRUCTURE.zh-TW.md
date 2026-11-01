# HyperDL 專案結構

本文件說明各資料夾的用途。資料夾用於歸類相關程式；原有 namespace 及應用程式行為不變。

## 應用程式

| 路徑 | 用途 |
|---|---|
| `App.xaml`、`App.xaml.cs` | WPF 應用程式啟動及共用資源。 |
| `MainWindow.xaml`、`MainWindow.xaml.cs` | 主視窗介面、UI 事件、下載清單協調及使用者訊息。 |
| `Core/Downloading/` | 下載引擎、佇列管理、工作模型、工作階段、分段、排程器、重試與頻寬限制、進度、範圍配置、metadata、檔案雜湊及檔名處理。 |
| `Core/Networking/` | HTTP 探測、HTTP/HTTPS 網址驗證、共用連線限制器及更新版本比較。 |
| `Core/Storage/` | SQLite 下載歷史／恢復資料及使用者設定保存。 |
| `Localization/` | 執行時本地化管理、XAML markup 擴充及英文／繁體中文字串表。 |
| `Platform/` | `hyperdl://` 協定註冊輔助。 |
| `Assets/` | 應用程式圖示（`app.ico`），用於執行檔與系統匣。 |
| `TextPromptWindow.cs` | 以程式碼建構的網址編輯對話框。 |

Core 類別維持 `HyperDL.Core` namespace。資料夾只改善檔案導覽，不會變更公開類別名稱或要求
namespace 遷移。

## 測試

| 路徑 | 用途 |
|---|---|
| `HyperDL.Tests/Downloading/` | 下載引擎、範圍計算及檔名處理測試。 |
| `HyperDL.Tests/Networking/` | 網址驗證、HTTP 探測及連線限制器測試。 |
| `HyperDL.Tests/Storage/` | SQLite 保存及恢復測試。 |
| `HyperDL.Tests/Localization/` | 字串表 key 對稱性及 UI 使用字串的覆蓋測試。 |
| `HyperDL.Tests/HyperDL.Tests.csproj` | 測試相依套件及對 WPF 專案的參照。 |

測試仍使用 `HyperDL.Tests` namespace。應用程式專案會排除整個 `HyperDL.Tests/` 目錄，不會
將測試程式編入正式應用程式。

## 發布及支援檔案

| 路徑 | 用途 |
|---|---|
| `scripts/publish.ps1` | Restore、Build、Test 並發布 Windows x64 self-contained 應用程式。 |
| `scripts/build-installer.ps1` | 發布應用程式並編譯 Inno Setup 安裝包。 |
| `scripts/test-installer.ps1` | 唯讀檢查安裝器設定及 Setup 檔案。 |
| `scripts/test-server.ps1` | Loopback HTTP range 測試伺服器，支援限速、延遲、429 與斷線等情境。 |
| `scripts/benchmark.ps1` | Loopback HTTP range 吞吐量基準測試。 |
| `scripts/build-extension.ps1` | 驗證並封裝 Chrome 擴充功能。 |
| `scripts/README.md` | 測試伺服器與基準測試的使用說明。 |
| `installer/HyperDL.iss` | Per-user 安裝、捷徑及卸載程式設定。 |
| `installer/Output/` | 產生的 Setup 輸出；Git 會忽略此目錄。 |
| `extension/` | Manifest V3 Chrome 擴充功能原始碼、README 與隱私權政策。 |
| `docs/user-guides/` | 繁體中文及英文使用指南與疑難排解文件。 |
| `ROADMAP.md` | 改進路線圖：各版本已完成範圍與候選待辦（繁體中文）。 |
| `RELEASE_NOTES_v1.2.0.md` | 目前版本的 Release Notes（雙語）。 |
| `docs/reference/` | 架構、協定、安全及未支援功能說明。 |
| `docs/testing/` | 安裝及解除安裝人工測試清單。 |
| `docs/images/` | 截圖指引及日後的應用程式截圖。 |
| `.github/` | Issue 及 Pull Request 範本、Release 檢查清單及 dependabot 設定。 |
| `.github/workflows/` | CI 建置測試、Release 及擴充功能封裝 workflows。 |

## 避免提交建置輸出

`bin/`、`obj/`、`artifacts/` 及 `TestResults/` 由本機建置產生，並已列入 Git 忽略清單。
Setup 安裝程式屬於發布產物，不必加入原始碼 Repository；可透過腳本建置，或附加到 GitHub Release。

其他語言版本：[English](PROJECT_STRUCTURE.md)。
