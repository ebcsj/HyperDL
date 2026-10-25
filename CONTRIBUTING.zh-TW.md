# 為 HyperDL 貢獻

## 開發環境

- Windows 10 或 Windows 11 x64。
- .NET 8 x64 SDK。
- 安裝 .NET Desktop Development 工作負載的 Visual Studio，或其他相容編輯器。
- PowerShell。

## 建置與測試

在 Repository 根目錄執行：

```powershell
& "C:\Program Files\dotnet\dotnet.exe" restore HyperDL.sln
& "C:\Program Files\dotnet\dotnet.exe" build HyperDL.sln -c Release
& "C:\Program Files\dotnet\dotnet.exe" test HyperDL.sln -c Release --no-build
```

## Pull Request

1. Fork Repository。
2. 建立範圍明確的功能分支。
3. 變更遵循現有 C# 及 XAML 風格。
4. 為行為變更新增或更新測試。
5. 執行 Release Build 及測試，並在 Pull Request 中附上結果。
6. 說明使用者可見的變更及已知限制。

請勿在 Pull Request 中加入 `bin`、`obj`、`artifacts`、執行階段資料庫、
`.part` 檔案、憑證、certificate 或私密金鑰。

## 安全性與範圍

貢獻內容不得加入 DRM 繞過、惡意程式碼、任意命令執行、遠端指令碼執行或侵犯著作權的功能。
下載的檔案不得自動執行。

其他語言版本：[English](CONTRIBUTING.md)。
