# FTP/FTPS 狀態與安全性

目前未實作 FTP 及 FTPS。HyperDL 只接受 HTTP 及 HTTPS 網址。

請勿將 FTP 憑證放在網址或日誌中。若加入 FTP 支援，應優先採用嚴格驗證憑證的 FTPS；
一般 FTP 會讓網路上的觀察者看到憑證及資料。憑證應存於作業系統憑證保存區，不應以明文寫入
設定檔或下載資料庫。

其他語言版本：[English](FTP_SECURITY.md)。
