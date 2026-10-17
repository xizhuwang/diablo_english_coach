# 本機 OCR／翻譯選型實測（2026-09-05）

## 結論

這台電腦優先採「英文 Windows OCR＋保守字詞修復／跨幀核對＋CPU INT8 專用翻譯」，忙碌 1 執行緒、有餘裕 2 執行緒。LLM 放在非即時的校正與教學工作，獨顯留給遊戲。

實測支持 CTranslate2 的 CPU 速度與低記憶體成本；目前已有 Argos 英中 1.9 模型可用，但其遊戲術語品質未達到直接上線標準。英文 Windows OCR 尚未安裝，ONNX 版 OPUS-MT、第二 OCR 與整合後端到端延遲尚未測試，不能宣稱已找出所有候選中的絕對最佳引擎。

本次只加入可重跑的診斷與報告，沒有更換正在執行的 publish-v012、修改使用者設定或發布新版本。

## 環境與負載

- Intel Core i7-1165G7：4 核心、8 邏輯處理器。
- Windows 回報實體記憶體 15.70 GiB，取樣時可用 3.56 GiB。
- NVIDIA MX330：2048 MiB VRAM，單次取樣已用 486 MiB，GPU 使用率 100%。另有 Intel Iris Xe；此次未測其推論效能。
- 14:49:08～14:49:21 的 CPU 五次取樣：71%、74%、64%、53%、84%；更早單次為 100%。
- 同期遊戲程序 CPU 增量約占全機 27.2%。這和系統 LoadPercentage 是不同計量方式，不能直接相加。
- 遊戲與教練程序均開著。沒有控制戰鬥場景、前景狀態、溫度或電源模式，也沒有測 FPS／幀時間。
- Ollama 當時載入 qwen3.5:0.8b Q8_0，size_vram=0（CPU 推論），模型佔用回報約 1.06 GB。

## 專用翻譯：CTranslate2 4.8.2／Argos en_zh 1.9

使用已下載的模型、CPU INT8、beam_size=2，含 tokenization、decode、繁體轉換；不含擷取、OCR、等待掃描與快取。每句之間停 250 ms，避免持續壓滿 CPU。

第一次為 10 個固定句子各重複三次，依 1→2→4 執行緒測試：

| 執行緒 | 樣本數 | P50 ms | P95 ms | 平均 CPU 時間 ms／句 |
|---|---:|---:|---:|---:|
| 1 | 30 | 233 | 445 | 171.9 |
| 2 | 30 | 110 | 158 | 195.9 |
| 4 | 30 | 108 | 256 | 342.2 |

另以 4→2→1 反向各測 10 句，確認遊戲負載／測試顺序可能造成的變動：

| 執行緒 | 樣本數 | P50 ms | P95 ms | 平均 CPU 時間 ms／句 |
|---|---:|---:|---:|---:|
| 4 | 10 | 122 | 513 | 442.1 |
| 2 | 10 | 140 | 183 | 214.2 |
| 1 | 10 | 140 | 210 | 137.5 |

P95 使用 nearest-rank；少量樣本是探索性數據。反向測試證實負載會影響速度，不應把首次差異全歸因於執行緒數。兩次中 4 執行緒都耗費更多 CPU，沒有穩定的尾端延遲優勢。第一次測試程序峰值工作集 173.6 MiB，模型載入 264～901 ms。

品質觀察：英文來源正確時也出現 summons→傳票、shard→堅硬之人、Leoric→獅子座。需要遊戲情境術語表、名稱保護及錯譯檢查；不是只修 OCR 就能解決。數字、否定詞、條件句須另列驗收案例。

## OCR 診斷

OcrEngine.AvailableRecognizerLanguages 只回傳 zh-Hant-TW。

使用五個程式產生的 Arial 24pt 白字深底短句，各重複三次；不是實際遊戲畫面品質測試。現有 2 倍縮放／對比前處理＋Windows OCR 的 P50=170.8 ms，P95=334.6 ms。精確整句相同 3/15（包含大小寫、空格、標點，因此不能解讀成 80% 的字辨識錯誤）。

- Follow Deckard Cain to Ashwold Cemetery. → FOIIow Deckard Cain to AshwoId Cemetery
- Do not open the gate until I return. → DO not open the gate untill return.
- I need your help. Follow me and stay close. → need your help. FOIlow me and stay close

固定錯誤會跨幀重複，因此文字一致只代表穩定，不等於正確。原始 bitmap 分支 15/15 於 OCR 呼叫出現 HRESULT 0x88982F50，其耗时是失敗時間，不可用來比較 OCR 速度；經現有前處理的分支 15/15 成功。

## 0.8B 英文 OCR 修復短測

只測一次、1 執行緒、CPU、think=false、最大輸出 48 tokens。送兩份含 y0ur／heIp／FoIIow 的英文候選，要求合併成一句。

- 端到端 11832 ms；Ollama 回報載入 5507 ms、prompt 2441 ms、生成 3732 ms。
- 輸出為 A、B 兩份都修好的句子，沒有遵守只回傳一份的格式。
- 與正在執行的教練共用 Ollama，包含載入／排程因素；不能視為純模型穩態 benchmark，也沒有足夠樣本計算 P95。
- 即使扣除載入，這次仍不適合進入一秒字幕的同步等待鏈。

## 建議配置與待驗收工作

1. 先補英文 Windows OCR，再拿實際字幕做比較；不因多幀一致就忽略字典異常、首詞缺漏、人名與數字。
2. 文字區先做低成本變化偵測，只有變化時 OCR。需要確認時安排短間隔重掃；避免等一般低頻 timer 的下一個完整週期。
3. CPU INT8 翻譯忙時 1 執行緒、CPU 有持續餘裕時 2 執行緒。保持模型常駐，單次請求只處理最新有效字幕。
4. 保留英文原始候選供修復；中文初稿不當成英文內容的依據。否定、數字、專有名詞與條件關係變動不能被相似度去重吞掉。
5. 低信心先重讀局部文字；仍不明確時，保留上一句並在必要時以短狀態標明尚待辨識，讓模型在背景提出可驗證修正。沒有畫面證據的缺詞不自行補寫。
6. 現行 CPU≥85% 時 OCR timer=2000 ms，未知字幕還需兩次確認；這個排程已足以超出一秒。0.5～1 秒只能作為整合後的目標，不能用翻譯模型的 110 ms 直接宣稱已達標。
7. 驗收需包含實際遊戲字幕、否定／數字／人名錯誤率、第一筆可用完整譯文延遲、取消率與遊戲幀時間。此次未收集 50 張真實字幕，沒有宣稱完成該項 A/B 測試。

## 重跑

- `scripts/test-local-translation.py --threads 1 2 4 --repeats 3 --pause 0.25 --output local-translation-test-hardware.json`（使用已存在的隔離 Python 3.12 套件）。
- 編譯後執行 `DiabloEnglishCoach.exe --ocr-benchmark local-translation-test-ocr-hardware.json`。只辨識程式產生的文字圖，不擷取螢幕。
- 原始量測檔放專案根目錄的 `local-translation-test*.json`，依既有 gitignore 留在本機。

官方文件：

- [Windows OCR 語言必須安裝後才能使用](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.availablerecognizerlanguages?view=winrt-26100)
- [CTranslate2 自動選擇 CPU 後端／指令集](https://opennmt.net/CTranslate2/hardware_support.html)
