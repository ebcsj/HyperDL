# HyperDL 專案結構

本文件說明各資料夾的用途。資料夾用於歸類相關程式；原有 namespace 及應用程式行為不變。

## 應用程式

| 路徑 | 用途 |
|---|---|
| `App.xaml`、`App.xaml.cs` | WPF 應用程式啟動及共用資源。 |
| `MainWindow.xaml`、`MainWindow.xaml.cs` | 主視窗介面、UI 事件、下載清單協調及使用者訊息。 |
| `Core/Downloading/` | 下載引擎、工作階段、分段、進度、範圍配置、metadata 及檔名處理。 |
| `Core/Networking/` | HTTP 探測、HTTP/HTTPS 網址驗證及共用連線限制器。 |
| `Core/Storage/` | SQLite 下載歷史／恢復資料及使用者設定保存。 |

Core 類別維持 `HyperDL.Core` namespace。資料夾只改善檔案導覽，不會變更公開類別名稱或要求
namespace 遷移。

## 測試

| 路徑 | 用途 |
|---|---|
| `HyperDL.Tests/Downloading/` | 下載引擎、範圍計算及檔名處理測試。 |
| `HyperDL.Tests/Networking/` | 網址驗證、HTTP 探測及連線限制器測試。 |
| `HyperDL.Tests/Storage/` | SQLite 保存及恢復測試。 |
| `HyperDL.Tests/HyperDL.Tests.csproj` | 測試相依套件及對 WPF 專案的參照。 |

測試仍使用 `HyperDL.Tests` namespace。應用程式專案會排除整個 `HyperDL.Tests/` 目錄，不會
將測試程式編入正式應用程式。

## 發布及支援檔案

| 路徑 | 用途 |
|---|---|
| `scripts/publish.ps1` | Restore、Build、Test 並發布 Windows x64 self-contained 應用程式。 |
| `scripts/build-installer.ps1` | 發布應用程式並編譯 Inno Setup 安裝包。 |
| `scripts/test-installer.ps1` | 唯讀檢查安裝器設定及 Setup 檔案。 |
| `installer/HyperDL.iss` | Per-user 安裝、捷徑及卸載程式設定。 |
| `installer/Output/` | 產生的 Setup 輸出；Git 會忽略此目錄。 |
| `docs/user-guides/` | 繁體中文及英文使用指南與疑難排解文件。 |
| `docs/reference/` | 架構、協定、安全及未支援功能說明。 |
| `docs/testing/` | 安裝及解除安裝人工測試清單。 |
| `docs/images/` | 截圖指引及日後的應用程式截圖。 |
| `.github/` | Issue 及 Pull Request 範本。 |

## 避免提交建置輸出

`bin/`、`obj/`、`artifacts/` 及 `TestResults/` 由本機建置產生，並已列入 Git 忽略清單。
Setup 安裝程式屬於發布產物，不必加入原始碼 Repository；可透過腳本建置，或附加到 GitHub Release。

其他語言版本：[English](PROJECT_STRUCTURE.md)。
