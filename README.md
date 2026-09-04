# Game English Coach

Windows 上的輕量英文教練長條，為 Diablo Immortal 的英文介面提供即時繁中翻譯、任務提示、遊戲攻略、遊戲連結式多益／數位 IC 英文，以及空閒時自動口說練習。

本專案是非官方、非營利的獨立工具，與 Blizzard Entertainment 沒有隸屬、贊助或背書關係。公開名稱刻意使用 **Game English Coach**；Diablo Immortal 只用來描述相容遊戲。程式不修改遊戲、不中介輸入、不讀取程序記憶體，也不包含任何遊戲素材。

## 最快安裝方式

適用 Windows 10/11 x64。GitHub Release 的壓縮檔已包含 .NET 執行環境，不必安裝 Visual Studio 或 .NET SDK。

1. 從 GitHub **Releases** 下載 `Game-English-Coach-Windows-x64.zip`，完整解壓縮。
2. 雙擊 `安裝並啟動.cmd`。
3. 安裝程式會下載官方 Ollama、約 1 GB 的快速翻譯模型、約 2 GB 的教練模型，以及約 40 MB 的 Vosk 英文口說模型，然後啟動教練。
4. 第一次安裝 Ollama 時可能出現 Windows 權限／安裝視窗；完成後同一個腳本會繼續。

安裝包預設強制本機模型使用 CPU，所以不依賴 NVIDIA CUDA、AMD ROCm 或特定 GPU 型號。Intel／AMD x64 CPU 都能使用；GPU 留給遊戲。建議至少 8 GB RAM、4 GB 可用磁碟空間；16 GB RAM 的體驗較穩定。Windows ARM、macOS、Linux 尚未提供一鍵包。

已安裝過模型時，可直接雙擊 `啟動英文教練.cmd`。只想分開安裝時也可使用：

- `安裝本機翻譯.cmd`：`qwen3.5:0.8b`
- `安裝本機模型.cmd`：`qwen3.5:2b-q4_K_M`
- `安裝口說模型.ps1`：Vosk `vosk-model-small-en-us-0.15`

## 遊戲內第一次設定

1. 將遊戲設為英文介面、英文語音、英文字幕及「視窗化全螢幕」。
2. 先開啟遊戲，再啟動教練。
3. 到「設定 → 辨識範圍」，分別框選下方中央的角色字幕區與左側任務目標區。
4. 選「測試兩個辨識區」，確認沒有把左下角聊天／交易頻道框進字幕區。
5. 按主畫面的「開啟」。

主視窗只保留翻譯、「開啟／關閉」與「設定」。拖曳翻譯文字可移動長條；設定可調寬度、透明度、聲音、角色與流派偏好。長翻譯會自動增加高度並提供捲動，不以省略號截斷。

## 即時翻譯與 OCR

- 對話字幕每個掃描週期優先處理；常見任務片語由本機規則立即翻譯，其餘交給本機 0.8B 模型並串流顯示第一段結果。
- 翻譯排程只保留最新一句，不使用 FIFO；舊字幕不應卡住新字幕。快取命中通常可立即顯示，但首次模型載入及未見過的長句無法保證 1 秒內完成。
- Windows OCR 後會移除獨立角色名、修正常見英文單字中的 `0/o` 混淆，並把簡體模型輸出轉成繁體中文。
- 左下角以 `[1]`、`[World]`、`[Trade]` 等開頭的聊天行、網址與常見買賣廣告會在翻譯前丟棄。這是降低干擾的規則，不是隱私隔離；字幕框仍應避開聊天區。
- 任務面板改為每 12 個掃描週期讀一次，而且只有文字真的變更才翻譯與產生指引。它會保留成遊戲進度背景，不會反覆念同一個目標。
- 翻譯快取最多 512 句，位於 `%LOCALAPPDATA%\DiabloEnglishCoach\fast-translations.json`。

若未安裝 English (United States) OCR，程式會退回繁中 Windows OCR。可在 Windows「語言與地區」加入英文 OCR 以提高準確度。

## 教練排程與 FIFO

三條工作彼此獨立：即時 OCR／翻譯、語音播放、慢速教練判斷。

- 預設每段教練講完只停 **6 秒**，設定可選 4／6／10 秒。
- 語音底層是 FIFO，一段開始後會完整講完；下一段最多排 3 筆。教練模型完成的任務判斷也使用最多 3 筆 FIFO，過時或太舊的遊戲狀態會在播放前丟棄。
- 長段落播放期間，OCR、翻譯及安全的背景教材準備可以繼續；不會等待整段播完才開始所有運算。
- 字幕翻譯刻意不用 FIFO，因為玩家只需要目前最新一句。
- 偵測到角色字幕／疑似過場時不開始新教練語音；已開始的短段落仍完整播完，避免被切成半句。
- 操作中或 CPU 忙碌時，複雜判斷最多用 1 thread；空閒且 CPU 低時最多用 4 threads 準備後續內容。預設 `num_gpu=0`。
- 慢速語言模型只負責任務判斷、情境化教學與教材多樣化；翻譯、快取和固定攻略不等它。

## 英文、攻略與多益 500–750

教學先從畫面出現的遊戲字詞開始，再隔幾段延伸同一個字在多益或數位 IC 的用法，不會突然插入無關課文。例如畫面出現 `confirm`，先理解 `Confirm your choice.`，之後才可能延伸 `Please confirm the delivery schedule.`。

內建連結詞包含 `increase`、`compare`、`require`、`available`、`confirm`、`avoid`、`return`、`complete`、`receive`、`select`、`provide`、`replace`、`reduce`、`collect`、`purchase`、`improve` 與數位 IC 的 `reset`。每個詞都有完整英文句、繁中句意與用法，而不是孤立背單字。

遊戲攻略採兩層：

- 本機固定原則：核心技能搭配、傷害／生存取捨、裝備效果閱讀，不呼叫模型。
- 死靈法師 PvE 參考快取：啟動時最多等 8 秒檢查固定公開來源，只擷取少量技能／裝備名稱；失敗時沿用上次快取。它不是背包掃描，也不宣稱當季最強。

設定可選角色、輕鬆 PvE／召喚／傷害／生存偏好，以及遊戲、多益、數位 IC 的延伸優先順序。延伸仍必須和近期遊戲單字有自然連結。

## 自動口說

口說預設關閉，必須在「設定 → 自動口說（空閒時）」閱讀說明後手動啟用。

- 開啟教練後約 **12 秒**開始找第一個安全空檔；之後預設每 60 秒，可選 30／60／120 秒。
- 口說比一般教材優先：預計 8 秒內到期時會保留下一個語音邊界，不再讓連續教材一直搶走機會。
- 需遊戲在前景、沒有角色字幕／過場、CPU <70%，且操作鍵與 Space 安靜至少 6 秒，再穩定約 3 秒。
- 優先練剛教過且有完整中文的短句；跟讀與看中文自行回答會交替出現。
- 示範完整播完才開麥克風。5 秒無聲跳過；有聲後安靜約 1.2 秒停止；最長 10 秒。
- 收音結束後立刻用本機 Vosk 辨識。若辨識超過約 1.2 秒，會播放已預備的相關短補充，讓等待時間仍有內容；開始的補充會完整說完才回饋。
- 教練逐字稿會跟著目前語音顯示；「設定 → 教練逐字稿」可查看最近 30 段。

收音只在記憶體中處理，不存檔、不上傳、不常駐監聽。若一直沒有觸發，可查看「設定 → 口說狀態／為何尚未邀請」，並確認 Vosk 模型、Windows 預設麥克風與桌面應用程式麥克風權限。

## 聲音與網路

預設自然女聲使用 Microsoft 的線上 Edge speech service，會把「要朗讀的文字」送到該服務；不需要 API key。若要全離線，在「設定 → 聲音與語速」取消「使用自然女聲」，改用電腦已安裝的 Windows 聲音。

翻譯預設是本機 Ollama，不上傳 OCR 文字。Azure Translator 只保留為使用者主動設定的備援；選用時才會把過濾後的英文送到 Microsoft，金鑰只存 Windows Credential Manager。

完整資料流程、刪除方式與網路端點請看 [PRIVACY.md](PRIVACY.md)。第三方授權與商標說明請看 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 效能與硬體相容性

- 發布包為 Windows x64 self-contained；CPU 品牌不限，不需要獨顯。
- 推論預設走 CPU，讓獨顯專心跑遊戲。若電腦同時有內顯與獨顯，Windows／遊戲可自行選高效能 GPU，教練不嘗試合併兩張 GPU 的記憶體。
- OCR 掃描會依 CPU 與鍵盤活動在約 0.35／0.65／2 秒間調整；任務區只做低頻掃描。
- 不使用全域鍵盤 Hook，只輪詢少量遊戲鍵並保存「距上次活動多久」，不保存按鍵內容。
- 關閉教練會停止 OCR、清空未播放語音並取消模型請求。

## 常見問題

### 找不到遊戲視窗

先啟動 Diablo Immortal、不要最小化，並使用視窗化全螢幕。

### 翻譯很慢

確認 Ollama 正在執行且已安裝 `qwen3.5:0.8b`。第一次載入模型通常最慢；常見片語和快取不需等待模型。字幕框越小、聊天越少，OCR 越快。

### 教練一直講或一直不講

到設定調整 4／6／10 秒停頓。角色對話、近期 Space、遊戲不在前景、CPU 高或口說保留時段都會延後新語音；目前段落不會被中途切斷。

### 自然語音失敗

程式會嘗試 Windows 離線聲音。也可直接關閉線上自然女聲，避免網路延遲和文字傳送。

### 防毒軟體警告

發布包是 CI 建置的未簽章 self-contained EXE，SmartScreen 可能警告。可從 GitHub Actions 對應提交重新建置。Ollama 安裝檔由官方下載並驗證 Windows 數位簽章；Vosk 模型使用固定 SHA-256。

## 開發與測試

需要 .NET 10 SDK：

```powershell
dotnet restore
dotnet build -c Release
dotnet run -- --self-test self-test.json
dotnet publish -c Release -r win-x64 --self-contained true -o publish-readable
```

硬體隔離的 `--self-test` 不開麥克風、不連真實翻譯 API。其他診斷：

- `--translation-test translation-live.json`：本機翻譯與快取
- `--personalized-teaching-test teaching-live.json`：真實 2B 教材生成
- `--speaking-test input.wav speaking-test.json`：使用 WAV 測 Vosk，不開麥克風
- `--guide-refresh-test guide-refresh-test.json`：固定攻略來源更新

GitHub Actions 在 main push 建立下載 artifact；推送 `v*` tag 時會另外建立 GitHub Release zip。Release 不包含大型模型，使用者第一次執行 `安裝並啟動.cmd` 時才從上游下載。

## 發佈與法律檢查

目前程式碼已檢查：沒有遊戲素材、沒有遊戲程序注入、沒有遙測、金鑰不進設定檔，且第三方套件都有宣告的開源授權。這不能合理地寫成「絕對不會有任何侵權或隱私問題」；商標、線上服務條款、外部攻略來源、模型版本及未來程式變更都可能改變風險。

正式公開前仍應由專案擁有者：

1. 選擇並加入本專案原始碼授權（目前未替擁有者擅自決定 MIT／GPL 等條款）。
2. 每次升級 NuGet 套件或模型後重新產生／檢查第三方 notices。
3. 保留非官方、無贊助關係的商標聲明，不使用 Blizzard 圖像作為 Logo。
4. 若要商業化，先取得專業法律審查及必要授權。

翻譯試驗與舊方案比較見 [TRANSLATION-PILOT.md](TRANSLATION-PILOT.md)。
