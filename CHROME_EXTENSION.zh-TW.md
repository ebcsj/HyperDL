# HyperDL Chrome 擴充功能

本指南說明如何下載並載入 HyperDL Chrome 擴充功能，以便透過瀏覽器將下載連結傳送至 HyperDL 應用程式。

## 下載

1. 從官方 GitHub Release 的附件中下載 `HyperDL-Chrome.zip`，或直接使用原始碼套件中的 `artifacts/extension/HyperDL-Chrome.zip`。
2. 將 ZIP 檔案解壓縮至本機資料夾（載入後需保留此資料夾）。

## 載入至 Chrome（開發人員模式）

1. 開啟 Chrome，前往 `chrome://extensions/`。
2. 開啟右上角的 **開發人員模式**。
3. 點擊 **載入未封裝項目**。
4. 選取解壓縮後的資料夾，確保資料夾內有 `manifest.json` 檔案。
5. 載入完成後，HyperDL 擴充功能就會顯示在擴充功能清單中。

## 使用方式

1. 確認 HyperDL 已安裝並正在執行。
2. 點擊 HyperDL 擴充功能圖示，即可將支援的下載連結透過 `hyperdl://` 協定傳送至 HyperDL。
3. 當瀏覽器提示開啟連結時，允許使用 HyperDL 開啟。

## 注意事項

- 此擴充功能並未上架至 Chrome 線上應用程式商店，必須使用開發人員模式載入。
- 適用於 Chrome、Chromium 系瀏覽器以及 Microsoft Edge（在 `edge://extensions/` 啟用開發人員模式後載入）。
- 必須先安裝 HyperDL 才能正確處理 `hyperdl://` 協定。
- 若有更新版本，請下載新版 ZIP 並替換擴充功能資料夾，然後在 `chrome://extensions/` 點擊 **重新載入** 即可。
