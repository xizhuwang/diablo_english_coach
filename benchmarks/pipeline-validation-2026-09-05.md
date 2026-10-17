# CPU 翻譯整合驗證（2026-09-05）

## 本機交付

- 新版：`publish-v013/DiabloEnglishCoach.exe`；`啟動英文教練.cmd` 已優先選取新版，舊版未刪除。
- CPU INT8 英中翻譯執行環境已安裝於 `translation-runtime`，安裝煙霧測試通過。模型、套件、快取不加入 Git。
- Windows 英文 OCR 安裝完成，程式實際回報 `en-US`，可用語言包含 `en-US` 與 `zh-Hant-TW`。
- 舊 LocalOllama 翻譯設定首次載入時遷移至 LocalCpu；既有 Azure／關閉選項不被覆蓋。辨識區、語音及口說同意保持原設定。
- 本次沒有終止舊教練、操作遊戲、錄音或重新啟動電腦。使用者仍須關閉舊教練後再開啟捷徑。

## 實作範圍

英文 OCR 原圖優先 → 異常才放大增強重讀 → 保守規則及兩幀詞序確認 → 本機片語／快取／CTranslate2 INT8 → 人名、數量、否定及部分關鍵意思檢查 → 繁中完整譯文。

翻譯只使用 CPU，依負載選 1～2 執行緒；程序保持常駐。換句只保留最新等待項，已送出的舊工作讀完後丟棄，不殺掉模型再重載。故障超過 8 秒才重啟該子程序。LLM 只在空閒時提出受限的英文 OCR 修正，不以中文初稿猜英文，也不能補寫漏詞。過期修正不更新畫面。

字幕確認間隔 180 ms；一般掃描 300／450／650 ms。新字幕的第二次確認不被任務框 OCR 阻擋。介面保留上次完整中文，不再顯示逐字串流的半句。

## 測試結果與量測條件

硬體沿用前份選型報告。下列是程式產生的 Arial 24pt 英文圖片，不是實際遊戲截圖。末輪查核時遊戲程序已不在執行，舊教練仍開啟，因此**不能當成打怪高負載或遊戲 FPS 驗收**。

| 測試 | 結果 |
|---|---|
| 編譯／發布 | 成功；NuGet 弱點資料源連線失敗，出現 NU1900 警告，不能宣稱完成依賴弱點稽核 |
| 內建 self-test | 通過，包含 OCR 規則、數量／否定、取消、快取及排程回歸 |
| UI preview | 退出碼 0；長中文、教練逐字稿、開啟與設定介面檢查通過 |
| 一鍵包結構／PowerShell 語法 | 通過；本機翻譯安裝煙霧測試也通過。未在另一台乾淨電腦執行全套下載 |
| 英文 OCR 原圖 | 5 句各 3 次，共 15/15 與原文完全一致；中位 5.61 ms，最大 60.05 ms |
| 英文 OCR 放大對比 | 15/15 完全一致；中位 35.65 ms，最大 39.81 ms，因此原圖優先 |
| OCR→地端譯文 | 6 句、4 輪，共 24/24 完成；包含 180 ms 二次確認，P50 268 ms、P95 312 ms、最小 195 ms、最大 381 ms |
| 程序暖機 | 四輪 597／518／541／518 ms；作業系統檔案快取已存在，不是重開機冷啟動數據 |
| 取消及切換執行緒 | 四輪均丟棄取消結果、保持相同程序，後續 Leoric 句子正確；切至 2 執行緒的下一筆 165／183／184／294 ms |

OCR→譯文時間**不包含螢幕擷取與首次輪詢等待**，且其中 2/6 句使用精確本機片語。不能推論所有實際遊戲字幕一秒內完成。四輪每輪使用不同快取檔，未將前輪譯文快取混入後輪量測；24 筆只有 6 個不同句子，不是 24 個獨立語料。

安裝英文 OCR 前的測試仍保留：繁中 OCR 把 `until I` 合併成 `untill`，被程式擋下；另有句首 `I` 漏字。五筆成功翻譯有一輪介於 0.59～2.41 秒，當時 Windows 安裝仍在背景進行。英文 OCR 後這組錯字消失，但前後負載不同，不能把全部速度差都歸因於 OCR 引擎。

人工檢查代表譯文：`Do not open the gate until I return.` →「在我回來之前不要開啟大門」；`Your summons deal 20% more damage.` →「你的召喚物造成的傷害提高 20%。」；`Leoric` 保留英文，未翻成「獅子座」。

## 可重現指令

```powershell
.\publish-v013\DiabloEnglishCoach.exe --self-test local-translation-test-regression.json
.\publish-v013\DiabloEnglishCoach.exe --ocr-benchmark local-translation-test-ocr.json
.\publish-v013\DiabloEnglishCoach.exe --pipeline-benchmark local-translation-test-pipeline-new.json
```

每次效能測試使用新的輸出檔名，避免同名 `.cache.json` 影響快取命中。原始本機結果為 `local-translation-test-pipeline-english1.json` 至 `english4.json`、`local-translation-test-ocr-english.json`、`local-translation-test-final5.json`；均由 gitignore 排除。

尚未量測：50 張真實遊戲字幕 A/B、不同遊戲字體／背景、打怪期間 P95、長句涵蓋率與遊戲幀時間。這些不計為已通過。
