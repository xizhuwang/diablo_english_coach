# CPU 翻譯新版發布驗證（2026-10-17）

## 結果

- .NET Release / Windows x64 self-contained 發布成功；重新連線 NuGet 還原後，最後一次編譯無警告、無錯誤。
- 本機 `--self-test`：246 項布林檢查全數通過。
- 本機 `--pipeline-benchmark`：6/6 合成字幕成功，英文 OCR 為 en-US，完整 OCR／確認／譯文耗時 254、267、201、264、296、248 ms。包含 180 ms 確認等待，不含遊戲畫面擷取或首次輪詢；2 句使用本機精確片語。暖機 506 ms。
- 取消舊翻譯、保持 worker 常駐及切換執行緒測試通過，最新 Leoric 句子名稱正確保留。
- `--ui-preview` 通過並人工檢視：翻譯、教練逐字稿及開啟／設定按鈕可見。
- Windows PowerShell 5.1 腳本語法與一鍵包結構檢查通過；以同一版本 PowerShell 重跑本機翻譯安裝，SHA256 驗證、解壓及真實翻譯煙霧測試通過。

## 本次修正

含中文的安裝腳本改為 UTF-8 BOM，修正 Windows 內建 PowerShell 5.1 以系統碼頁解讀 UTF-8 而產生語法錯誤的問題。翻譯下載使用 `-UseBasicParsing`。GitHub Actions 加入 PowerShell 5.1 語法／安裝包驗證，以及乾淨 runner 下載與測試 CPU 翻譯執行環境的發布前檢查。

## 測試環境限制

初次在受限沙箱執行時，.NET bundle 解壓及測試快取搬移遭權限拒絕；這些失敗未計為通過。改在一般 Windows 使用者環境執行上述測試後通過。

沒有操作遊戲或麥克風。未重新驗證真實遊戲戰鬥幀率、所有字幕字體、自然語音網路服務或完整口說錄音往返。測試通過代表已覆蓋項目未發現失敗，不代表所有遊戲情境零錯誤。GitHub 建置及發布狀態以 Actions／Releases 實際結果為準。

本機原始報告為 `local-translation-test-release-final-20261017.json` 與 `local-translation-test-release-pipeline-final-20261017.json`，不隨原始碼上傳。模型權重、語音模型、快取、使用者設定與金鑰不加入版本控制。

## 乾淨環境驗證與版本紀錄

最初 v0.1.3 建置在 PowerShell 管線的翻譯安裝試跑失敗，發布閘門正常擋下，沒有產生 Release 下載包。修正 worker 接受 UTF-8 BOM，回應使用 ASCII JSON escapes 避免舊主控台碼頁影響，並補充安裝錯誤細節。

修正版 `38e6976` 的 [GitHub Actions 全新 Windows 環境驗證](https://github.com/xizhuwang/diablo_english_coach/actions/runs/39034550730)已成功：編譯、PowerShell 5.1 安裝包檢查、從官方來源下載／驗證／安裝 CPU 翻譯依賴及實際翻譯均通過。保留失敗的 v0.1.3 標籤，不改寫歷史；後續修正版發布為 v0.1.4。
