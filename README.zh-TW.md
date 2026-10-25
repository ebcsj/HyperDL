# HyperDL Windows 安裝程式

`HyperDL.iss` 定義 per-user x64 安裝程式。應用程式安裝於
`%LOCALAPPDATA%\Programs\HyperDL`，不需要系統管理員權限。Inno Setup 管理解除安裝註冊及
實際解除安裝程式；開始功能表的解除安裝捷徑使用 `{uninstallexe}` 常數，不會重新命名產生的
執行檔。

安裝程式沒有定義解除安裝時刪除資料的區段。一般解除安裝會保留使用者下載內容、安裝目錄以外
的 `.part` 檔案，以及 `%LOCALAPPDATA%\HyperDL` 中的設定與下載歷史。

## 需求

- Windows x64。
- 位於 `C:\Program Files\dotnet\dotnet.exe` 的 .NET 8 SDK。
- Inno Setup 6。

## 建置

在專案根目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

指令碼會先 restore、build、test 並發布 HyperDL，再尋找 Inno Setup 並編譯安裝程式。輸出路徑：

```text
installer\Output\HyperDL_Setup.exe
```

指令碼會顯示檔案大小及 SHA-256 checksum。若只要執行安裝程式靜態檢查而不安裝應用程式：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-installer.ps1
```

## 人工安裝及解除安裝檢查

1. 使用測試帳戶安裝，確認預設 per-user 路徑。
2. 確認開始功能表的應用程式及 `Uninstall HyperDL` 捷徑。
3. 確認 Windows「已安裝的應用程式」列出 HyperDL。
4. 啟動應用程式並檢查 About／檔案版本及發行者。
5. 從 Windows 設定解除安裝，並另外測試開始功能表捷徑。
6. 確認應用程式檔案及捷徑已移除，而使用者下載、部分下載、設定及下載歷史仍保留。
7. 重新安裝，並測試覆蓋既有安裝的升級流程。

Inno Setup 會選擇產生的解除安裝程式檔名，通常為 `unins000.exe`。請勿重新命名；
應使用 Inno Setup 管理的 `{uninstallexe}` 參照。請勿使用真實使用者資料測試破壞性資料刪除。

發布前，請人工驗證乾淨安裝、升級、捷徑、Windows「已安裝的應用程式」註冊、下載及恢復流程，
以及兩種解除安裝入口。本專案未替安裝程式簽署程式碼。

其他語言版本：[English](README.md)。
