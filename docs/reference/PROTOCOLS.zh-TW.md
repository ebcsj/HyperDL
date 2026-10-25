# 支援的協定

## 已實作

- HTTP 及 HTTPS 直接檔案下載。
- 若伺服器確認支援 byte ranges 並提供已知的正長度，會探測 HTTP Range 並使用分段要求。
- 無法安全選擇分段下載時，回退為單一連線。

## 尚未實作

- FTP 或 FTPS。請勿在 HTTP 網址中輸入 FTP 憑證，也不要預期 FTP 網址可用。
- BitTorrent、magnet 連結或 tracker 操作。
- 動態分段或自適應連線控制。
- 更新資訊來源或更新套件安裝。

不會繞過 DRM 或受保護服務的存取控制。

其他語言版本：[English](PROTOCOLS.md)。
