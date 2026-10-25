# HyperDL 疑難排解

[繁體中文](TROUBLESHOOTING.md) | [English](TROUBLESHOOTING.en.md)

## 網址被拒絕

- 只支援絕對 HTTP 或 HTTPS 網址。請使用直接指向檔案的 URL，而不是網站首頁、
  登入頁、FTP 位址或磁力連結。
- 確認網址沒有空白、拼字錯誤或內嵌帳密。HyperDL 不提供輸入網站 Cookie 或
  Authorization token 的功能。
- 有些網站會在探測或下載時要求登入、驗證碼或特定瀏覽器工作階段；這些情況
  不屬於支援範圍。

## 下載失敗或速度為零

- 確認伺服器仍可連線、網址仍有效，並允許目前網路取得該檔案。
- 檢查 Windows 防火牆、代理伺服器、VPN 或網路過濾設定是否阻擋連線。
- 確認下載磁碟有足夠空間，且目前使用者對下載資料夾有寫入權限。
- 若伺服器不支援 Range，HyperDL 會改用單一連線。這不是錯誤，但可能比多段下載慢。
- 若進度總長度未知，代表伺服器沒有提供可用的 `Content-Length`；下載仍可進行，
  但百分比與 ETA 可能不可用。

## 無法暫停或繼續

Pause/Continue 只支援已確認 Range 能力、檔案大小已知的分段下載。單連線 fallback
不支援續傳。若伺服器拒絕 Range、檔案長度未知或探測結果不可靠，請讓下載完成，
或取消後重新開始。

續傳前，HyperDL 會再次探測來源並比較可用的檔案大小、ETag、Last-Modified 及
Range 資訊。如果來源已變更或無法重新確認，下載會停止，以避免把不同版本的資料
拼在一起。

## 找不到下載檔或 `.part` 暫存檔

- 開啟設定確認下載資料夾；選取工作後使用 `開啟下載資料夾`。
- 完成的檔案可能因同名檔案而使用帶序號的檔名。
- `.part` 是未完成的暫存檔。取消下載會移除該工作的未完成資料；刪除可恢復記錄時，
  UI 會詢問是否同時移除 `.part`。不要手動改名或移動正在使用的暫存檔。
- 單連線下載不會保存可續傳 session。若程式在非預期情況下關閉，其未完成內容不能
  透過恢復清單續傳。

## 下載歷史或設定無法載入

HyperDL 將設定及下載 metadata 保存在 `%LOCALAPPDATA%\HyperDL`。若 UI 顯示資料庫或
設定錯誤，先關閉應用程式並備份該資料夾，再檢查使用者帳戶的磁碟空間與資料夾權限。
不要在應用程式執行時刪除 SQLite 主檔、`-wal` 或 `-shm` 檔案。

## 建置或安裝問題

- 開發建置需要 Windows x64 和 .NET 8 SDK；執行 `dotnet restore HyperDL.sln` 後再
  執行 `dotnet build HyperDL.sln -c Release`。
- 建立 Setup 需要 Inno Setup 6。若找不到編譯器，先安裝該工具後重試
  `scripts\build-installer.ps1`。
- Setup 未簽署時，Windows 可能顯示 SmartScreen 警告。只執行從可信來源取得的安裝程式。
- 若安裝器或應用程式無法啟動，請記下 Windows 版本、錯誤訊息及重現步驟；分享記錄前
  應移除 URL 中可能包含的私人資訊。

更多狀態資訊請參閱[支援協定](../reference/PROTOCOLS.md)、
[使用指南](USER_GUIDE.md)及[安裝測試清單](../testing/INSTALL_UNINSTALL_TEST_CHECKLIST.md)。
