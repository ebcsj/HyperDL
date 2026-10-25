# 第三方套件聲明

本文件列出專案使用的直接 NuGet 相依套件及解析後的執行階段套件。準備本文件時已檢查套件
metadata 及本機 NuGet 套件授權檔。套件版本固定於專案檔或解析後的相依性圖；本次文件工作
未新增套件。

## 應用程式執行階段

| 套件 | 版本 | 用途 | 授權 | 官方專案／授權網址 | Windows x64 發布內容 | 聲明 |
|---|---:|---|---|---|---|---|
| Microsoft.Data.Sqlite | 8.0.11 | SQLite ADO.NET Provider metapackage | MIT | [專案](https://learn.microsoft.com/dotnet/standard/data/sqlite/)、[授權](https://licenses.nuget.org/MIT) | 已解析至應用程式發布內容；實作組件來自 `Microsoft.Data.Sqlite.Core` | 保留 MIT 聲明 |
| Microsoft.Data.Sqlite.Core | 8.0.11 | SQLite ADO.NET Provider 實作 | MIT | [專案](https://learn.microsoft.com/dotnet/standard/data/sqlite/)、[授權](https://licenses.nuget.org/MIT) | 是 | 保留 MIT 聲明 |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.6 | SQLite 原生 Provider 套件組 | Apache-2.0 | [專案](https://github.com/ericsink/SQLitePCL.raw)、[授權](https://licenses.nuget.org/Apache-2.0) | 是 | 保留 Apache-2.0 聲明 |
| SQLitePCLRaw.core | 2.1.6 | SQLitePCLRaw 核心 API | Apache-2.0 | [專案](https://github.com/ericsink/SQLitePCL.raw)、[授權](https://licenses.nuget.org/Apache-2.0) | 是 | 保留 Apache-2.0 聲明 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.6 | 內含 SQLite 原生程式庫的套件 | 套件 metadata 為 Apache-2.0；SQLite 引擎為公有領域 | [專案](https://github.com/ericsink/SQLitePCL.raw)、[套件授權](https://licenses.nuget.org/Apache-2.0)、[SQLite](https://www.sqlite.org/copyright.html) | 是 | 保留套件聲明；重新散布時檢查套件內附的第三方聲明 |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.6 | SQLitePCLRaw 原生 Provider | Apache-2.0 | [專案](https://github.com/ericsink/SQLitePCL.raw)、[授權](https://licenses.nuget.org/Apache-2.0) | 是 | 保留 Apache-2.0 聲明 |
| System.Memory | 4.5.3 | Memory API 相容性相依套件 | MIT（套件授權檔） | [專案](https://github.com/dotnet/corefx)、[授權](https://github.com/dotnet/corefx/blob/master/LICENSE.TXT) | 已解析為執行階段相依套件；散布時保留聲明 | 保留 MIT 聲明 |

## 僅供測試使用的直接相依套件

這些套件用於建置及執行 `HyperDL.Tests`，不會包含於應用程式 Windows x64 發布輸出。

| 套件 | 版本 | 用途 | 授權 | 官方專案／授權網址 | 應用程式發布內容 | 聲明 |
|---|---:|---|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.8.0 | .NET 測試執行 | MIT | [專案](https://github.com/microsoft/vstest)、[授權](https://github.com/microsoft/vstest) | 否 | 重新散布測試工具時保留 MIT 聲明 |
| xunit | 2.5.3 | 單元測試框架 | Apache-2.0 | [專案](https://xunit.net/)、[授權](https://licenses.nuget.org/Apache-2.0) | 否 | 保留 Apache-2.0 聲明 |
| xunit.runner.visualstudio | 2.5.3 | Visual Studio 測試執行器 Adapter | Apache-2.0 | [專案](https://github.com/xunit/visualstudio.xunit)、[授權](https://licenses.nuget.org/Apache-2.0) | 否 | 保留 Apache-2.0 聲明 |

測試 SDK 亦會解析 Microsoft Test Platform 元件及 xUnit 傳遞相依套件。本機套件 metadata
指出 Microsoft TestPlatform 元件使用 MIT，而大多數 xUnit 元件使用 Apache-2.0。
`xunit.abstractions` 2.0.3 的套件 metadata 有授權網址，但未提供 SPDX 授權識別；重新散布測試
二進位檔時，請確認並保留其上游授權聲明。

## 備註

- 應用程式專案直接參照 `Microsoft.Data.Sqlite` 8.0.11。解析後的 SQLitePCLRaw 執行階段套件
  已列於上表。
- 測試專案直接參照 `Microsoft.NET.Test.Sdk` 17.8.0、`xunit` 2.5.3 及
  `xunit.runner.visualstudio` 2.5.3。
- 未加入 FluentFTP、BitTorrent 引擎、更新器或其他原生相依套件。
- 專案的 MIT 授權不會取代第三方授權或聲明義務。本清單不是法律意見；重新散布二進位檔前，
  請由人工完整審查。

其他語言版本：[English](THIRD_PARTY_NOTICES.md)。
