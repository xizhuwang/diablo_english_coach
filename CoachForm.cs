using System.Text;

namespace DiabloEnglishCoach;

internal sealed class CoachForm : Form
{
    private readonly CoachConfig _config = CoachConfig.Load();
    private readonly bool _previewMode;
    private readonly OcrService _ocrService;
    private readonly CoachService _coachService = new();
    private readonly SpeechService _speechService;
    private readonly AdaptiveLoadMonitor _loadMonitor = new();
    private readonly IdleLessonPlanner _idleLessons = new();
    private readonly BuildGuideCache _buildGuides = new();
    private readonly SpeakingPlanner _speakingPlanner = new();
    private readonly System.Windows.Forms.Timer _speakingGuard = new() { Interval = 250 };
    private CancellationTokenSource? _speakingCts;
    private long _lastTeachingAt;
    private long _lastSpeakingScreenCheck;
    private byte[]? _speakingScreen;
    private bool _speakingFaulted;
    private ContextMenuStrip? _settingsMenu;
    private bool _settingsOpen;
    private int _runVersion;
    private long _lastDialogueAt;
    private readonly NarrationGuard _narrationGuard = new();
    private CoachReply? _readyReply;
    private Action? _retryRequest;
    private bool _speakingDemonstration;
    private bool _skipSpeakingResponse;
    private readonly System.Windows.Forms.Timer _scanTimer = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _translationCts;
    private WindowInfo? _gameWindow;
    private bool _captureBusy;
    private bool _running;
    private bool _positionAtGameOnShown;
    private bool _coachBusy;
    private int _scanSequence;
    private int _questGuidanceCount;
    private string _candidate = string.Empty;
    private int _candidateCount;
    private string _questCandidate = string.Empty;
    private int _questCandidateCount;
    private string _currentDialogue = string.Empty;
    private string _pendingDialogue = string.Empty;
    private string _pendingQuest = string.Empty;
    private readonly Queue<string> _recentSentences = new();
    private readonly Queue<string> _recentQuests = new();

    private readonly Label _statusLabel = new();
    private readonly Label _originalLabel = new();
    private readonly Label _simpleLabel = new();
    private readonly Label _chineseLabel = new();
    private readonly Panel _translationViewport = new() { AutoScroll = true };
    private bool _translationLayoutReady;
    private bool _layingOutTranslation;
    private readonly Label _keywordsLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _settingsButton = new();
    private const int ExpandedHeight = 54;
    private const int CompactHeight = 54;
    private string _currentQuest = string.Empty;
    private long _lastQuestSeenAt;
    private int _translationVersion;
    private string _translationText = "翻譯 · 按開啟開始（可拖曳這裡移動）";
    private readonly FastTranslationService _fastTranslation = new();
    private readonly System.Windows.Forms.Timer _teachingTimer = new() { Interval = 1000 };
    private long _nextModelLessonAt;
    private long _nextPersonalizationAt;
    private bool _personalizationBusy;
    private CancellationTokenSource? _personalizationCts;
    private (string Text, bool Quest, int Version)? _queuedTranslation;
    private bool _translating;
    private (string Text, bool Quest)? _retryTranslation;
    private long _translationRetryAt;
    private Func<string, bool, Task<TranslationResult>>? _previewTranslate;

    public CoachForm(bool previewMode = false)
    {
        _previewMode = previewMode;
        // Migrate only the old broad lower-screen ROI, which included public chat.
        if (_config.OverlayLayoutVersion < 2)
        {
            if (_config.RegionX < .15 && _config.RegionWidth > .7 && _config.RegionHeight > .25)
            {
                _config.RegionX = .27; _config.RegionY = .64;
                _config.RegionWidth = .46; _config.RegionHeight = .24;
            }
            _config.WindowLeft = _config.WindowTop = -1;
            _config.OverlayLayoutVersion = 2;
        }
        _idleLessons.Guides = _buildGuides;
        _coachService.BuildGuides = _buildGuides;
        _ocrService = new OcrService();
        _speechService = new SpeechService(_config, initializeLocalVoice: !previewMode);
        _speechService.MayStartPlayback = NarrationEnvironmentClear;
        _speechService.StatusChanged += message => SetStatus(message);
        InitializeUi();
        _scanTimer.Interval = Math.Clamp(_config.ScanIntervalMs, 700, 5000);
        _scanTimer.Tick += ScanTimerTick;
        _teachingTimer.Tick += (_, _) => TeachingTick();
        _speakingGuard.Tick += (_, _) => CheckSpeakingSafety();
        if (!previewMode)
            Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WdaExcludeFromCapture);
        ApplyRoundedCorners();
    }

    private void InitializeUi()
    {
        Text = "Diablo English Coach";
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var overlayWidth = Math.Min(area.Width - 20, _config.TranslationWidth > 0
            ? _config.TranslationWidth : Math.Clamp((int)Math.Round(area.Width * 0.50), 640, 1100));
        Size = new Size(overlayWidth, ExpandedHeight);
        MinimumSize = new Size(480, CompactHeight);
        MaximumSize = new Size(1600, 300);
        var defaultLocation = new Point(
            area.Left + (area.Width - Width) / 2,
            Math.Max(area.Top, area.Bottom - Height - 10));
        var useSavedLocation = SavedLocationIsVisible(area) && !SavedLocationOverlapsEnemyHud(area);
        _positionAtGameOnShown = !useSavedLocation;
        Location = useSavedLocation
            ? new Point(_config.WindowLeft, _config.WindowBottom >= 0 ? _config.WindowBottom - Height : _config.WindowTop)
            : defaultLocation;
        TopMost = true;
        ShowInTaskbar = false;
        Opacity = Math.Clamp(_config.WindowOpacity, 0.55, 1.0);
        BackColor = Color.FromArgb(22, 24, 31);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 9);
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);
        Resize += (_, _) => ApplyRoundedCorners();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(7, 4, 7, 5),
            ColumnCount = 1,
            RowCount = 1,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = BackColor
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(topBar, 0, 0);

        ConfigureContentLabel(_chineseLabel, _translationText, Color.FromArgb(167, 243, 208), 10, FontStyle.Regular);
        _chineseLabel.Dock = DockStyle.None;
        _chineseLabel.Margin = Padding.Empty;
        _chineseLabel.AutoEllipsis = false;
        _chineseLabel.UseMnemonic = false;
        _chineseLabel.TextAlign = ContentAlignment.TopLeft;
        _chineseLabel.Cursor = Cursors.SizeAll;
        _chineseLabel.MouseDown += DragOverlay;
        _translationViewport.Dock = DockStyle.Fill;
        _translationViewport.Margin = Padding.Empty;
        _translationViewport.Controls.Add(_chineseLabel);
        topBar.Controls.Add(_translationViewport, 0, 0);
        _chineseLabel.TextChanged += (_, _) => ReflowTranslation(resetScroll: true);
        _translationViewport.SizeChanged += (_, _) => ReflowTranslation();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 7, 0, 0)
        };
        var tips = new ToolTip { InitialDelay = 250, ReshowDelay = 100 };
        ConfigureCompactButton(_startButton, "開啟", "開啟／關閉自動教練", (_, _) => ToggleRunning(), tips);
        ConfigureCompactButton(_settingsButton, "設定", "辨識、聲音、顯示與流派設定", (_, _) => ShowSettingsMenu(), tips);
        toolbar.Controls.AddRange(new Control[]
        {
            _startButton, _settingsButton
        });
        topBar.Controls.Add(toolbar, 1, 0);
        tips.SetToolTip(_chineseLabel, "拖曳移動；長文自動換行，超過四行可用右側捲軸閱讀。設定可調整寬度。");

        _translationLayoutReady = true;
        PerformLayout();
        ReflowTranslation(resetScroll: true);
        ApplyRoundedCorners();
    }

    private void ReflowTranslation(bool resetScroll = false)
    {
        if (!_translationLayoutReady || _layingOutTranslation || _translationViewport.ClientSize.Width <= 0) return;
        _layingOutTranslation = true;
        try
        {
            var bottom = Bottom;
            // Reserve scrollbar width even for short text so adding a scrollbar
            // cannot change wrapping and repeatedly resize the window.
            var outerWidth = _translationViewport.Width;
            var labelWidth = Math.Max(80, outerWidth - SystemInformation.VerticalScrollBarWidth - 2);
            var textWidth = Math.Max(40, labelWidth - _chineseLabel.Padding.Horizontal);
            var measured = TextRenderer.MeasureText(_chineseLabel.Text, _chineseLabel.Font,
                new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            var lineHeight = TextRenderer.MeasureText("國Ag", _chineseLabel.Font).Height;
            var textHeight = measured.Height + _chineseLabel.Padding.Vertical + 4;
            // Child SizeChanged can run halfway through a TableLayout pass. Its
            // current Height is not a reliable way to infer the parent's padding.
            var chrome = Padding.Vertical + (_translationViewport.Parent?.Parent?.Padding.Vertical ?? 9);
            var visibleHeight = Math.Min(textHeight, lineHeight * 4 + _chineseLabel.Padding.Vertical + 4);
            var targetHeight = Math.Max(CompactHeight, visibleHeight + chrome);
            if (Height != targetHeight)
            {
                Height = targetHeight;
                var screen = Screen.FromRectangle(Bounds).WorkingArea;
                Top = Math.Clamp(bottom - Height, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
            }
            _chineseLabel.Size = new Size(labelWidth, textHeight);
            _translationViewport.AutoScrollMinSize = new Size(0, textHeight);
            if (resetScroll) _translationViewport.AutoScrollPosition = Point.Empty;
        }
        finally { _layingOutTranslation = false; }
    }

    private void SetTranslationWidth(int width)
    {
        var screen = Screen.FromRectangle(Bounds).WorkingArea;
        _config.TranslationWidth = width;
        Width = Math.Min(width, screen.Width - 20);
        ReflowTranslation();
        ClampToVisibleScreen();
        _config.Save();
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        _ = UpdateBuildGuidesAsync(); // Small public text fetch; never block OCR/model startup.
        _gameWindow = CaptureService.FindDiabloWindow();
        if (_gameWindow is not null && _positionAtGameOnShown)
            PositionOverGameWindow(_gameWindow.ClientBounds);
        var model = await _coachService.CheckAsync(_config, _lifetimeCts.Token);
        var game = _gameWindow is null ? "尚未找到 Diablo Immortal 視窗" : "已找到遊戲視窗";
        SetStatus($"{game} · {model.Message}");
        if (_config.TranslationProvider == TranslationProviders.LocalOllama)
            _ = WarmLocalTranslationAsync();
    }

    private async Task WarmLocalTranslationAsync()
    {
        if (_translating || IsDisposed) return;
        _translating = true;
        try { await _fastTranslation.TranslateAsync("Ready.", false, _config, _lifetimeCts.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            _translating = false;
            if (_queuedTranslation is { } waiting && _running && !IsDisposed)
            {
                _queuedTranslation = null;
                _ = TranslateVisibleAsync(waiting.Text, waiting.Quest);
            }
        }
    }

    private void ToggleRunning()
    {
        _running = !_running;
        _runVersion++;
        _translationVersion++;
        _queuedTranslation = null;
        _retryTranslation = null;
        _fastTranslation.Cancel();
        _teachingTimer.Enabled = _running && !_previewMode;
        _nextModelLessonAt = Environment.TickCount64 + 15_000;
        _nextPersonalizationAt = Environment.TickCount64 + 8_000;
        _loadMonitor.SetEnabled(_running);
        _idleLessons.Reset(Environment.TickCount64);
        _speakingPlanner.Reset(Environment.TickCount64);
        _speakingFaulted = false;
        CancelSpeaking();
        if (!_running)
        {
            CancelPersonalization();
            _translationCts?.Cancel();
            _retryRequest = null;
            _readyReply = null;
            _pendingDialogue = _pendingQuest = string.Empty;
            _speechService.Stop();
            _recentSentences.Clear();
            _recentQuests.Clear();
        }
        _startButton.Text = _running ? "關閉" : "開啟";
        _startButton.ForeColor = _running ? Color.FromArgb(248, 113, 113) : Color.FromArgb(167, 243, 208);
        _scanTimer.Enabled = _running && !_previewMode;
        SetStatus(_running ? "已啟用：多益／數位 IC／遊戲英文輪替；空閒時由模型準備個人化教材。" : "已暫停");
        if (_running && !_previewMode)
            _ = ScanOnceAsync(force: false);
        BeginInvoke(RestoreGameFocus);
    }

    private async void ScanTimerTick(object? sender, EventArgs eventArgs) => await ScanOnceAsync(force: false);

    private async Task ScanOnceAsync(bool force)
    {
        if (_speakingCts is not null || _captureBusy || (!force && (!_running || _settingsOpen)))
            return;
        var runVersion = _runVersion;
        _captureBusy = true;

        try
        {
            var load = _loadMonitor.Sample(false);
            _config.InferenceThreads = AdaptiveLoadMonitor.InferenceBudget(load, Environment.ProcessorCount);
            _scanTimer.Interval = load.CpuPercent >= 85 ? 2000 : load.ShouldDefer ? 1100 : 700;
            if (_gameWindow is null || !CaptureService.TryRefresh(_gameWindow, out var refreshed))
            {
                _gameWindow = CaptureService.FindDiabloWindow();
                if (_gameWindow is null)
                {
                    _translationCts?.Cancel();
                    _idleLessons.Reset(Environment.TickCount64);
                    SetStatus("找不到 Diablo Immortal。請先開啟遊戲並使用視窗化全螢幕。");
                    return;
                }
            }
            else
            {
                _gameWindow = refreshed;
            }

            var gameForeground = NativeMethods.GetForegroundWindow() == _gameWindow.Handle;
            if (!force && (!gameForeground || load.ShouldDefer))
            {
                _speakingPlanner.TryNext(Environment.TickCount64, _config.AutoSpeakingEnabled, false);
                // Let the current paragraph finish. Busy state only delays the NEXT one.
                // Neither CPU load nor new action keys cancel current inference.
                // Leaving the game does cancel it.
                if (_coachBusy && !gameForeground)
                    _translationCts?.Cancel();
                if (!gameForeground)
                    return;
            }

            var dialogueText = await RecognizeRegionAsync(CaptureRegionKind.Dialogue);
            // Quest objectives usually stay on screen much longer than dialogue.
            // Reading them every third cycle keeps game-time CPU usage modest while
            // still noticing a new objective within a few seconds.
            var shouldScanQuest = force || _scanSequence++ % 3 == 0;
            var questText = _config.QuestRegionConfigured && shouldScanQuest
                ? await RecognizeRegionAsync(CaptureRegionKind.Quest)
                : string.Empty;

            if (IsDisposed || runVersion != _runVersion || (!force && (!_running || _settingsOpen)))
                return;
            if (!force && shouldScanQuest)
            {
                var questVisible = OcrService.LooksLikeEnglishSubtitle(questText);
                if (questVisible)
                    _lastQuestSeenAt = Environment.TickCount64;
                _narrationGuard.ObserveQuestPanel(Environment.TickCount64, questVisible);
                if (!questVisible && Environment.TickCount64 - _lastQuestSeenAt > 10_000)
                    _currentQuest = _pendingQuest = string.Empty;
            }

            if (force)
            {
                // Settings diagnostics must not turn the coach on or call LLM/TTS.
                SetStatus($"辨識測試：對話{(OcrService.LooksLikeEnglishSubtitle(dialogueText) ? "✓" : "－")} · 任務{(OcrService.LooksLikeEnglishSubtitle(questText) ? "✓" : "－")}");
                _originalLabel.Text = $"QUEST · {questText}";
                _simpleLabel.Text = dialogueText;
                MessageBox.Show(this, $"任務：{questText}\n\n對話：{dialogueText}", "辨識測試");
                return;
            }

            // Refresh the budget after OCR; inference has an independent timer.
            load = _loadMonitor.Sample(false);
            if (NativeMethods.GetForegroundWindow() != _gameWindow.Handle)
                return;

            var dialogueVisible = OcrService.LooksLikeEnglishSubtitle(dialogueText);
            if (dialogueVisible)
            {
                _lastDialogueAt = Environment.TickCount64;
            }
            _narrationGuard.ObserveDialogue(Environment.TickCount64, dialogueVisible);
            _narrationGuard.ObserveSpaceKey(Environment.TickCount64, _loadMonitor.DialogueKeyIdleMs);
            var deferCoaching = load.ShouldDefer;
            HandleRecognizedText(dialogueText, CaptureRegionKind.Dialogue, deferCoaching, load.Reason);
            HandleRecognizedText(questText, CaptureRegionKind.Quest, deferCoaching, load.Reason);

            // Teaching has its own timer; OCR never waits for inference or speech.
        }
        catch (OperationCanceledException)
        {
            // Expected while closing or replacing a translation.
        }
        catch (Exception exception)
        {
            SetStatus($"擷取失敗：{exception.Message}");
        }
        finally
        {
            _captureBusy = false;
        }
    }

    private async Task<string> RecognizeRegionAsync(CaptureRegionKind kind)
    {
        if (_gameWindow is null)
            return string.Empty;
        using var capture = CaptureService.CaptureRegion(_gameWindow, _config, kind);
        using var prepared = CaptureService.PrepareForOcr(capture);
        return await _ocrService.RecognizeAsync(prepared, _lifetimeCts.Token);
    }

    private bool HandleRecognizedText(
        string text,
        CaptureRegionKind kind,
        bool deferCoaching,
        string loadReason)
    {
        if (!OcrService.LooksLikeEnglishSubtitle(text))
            return false;

        if (kind == CaptureRegionKind.Dialogue)
        {
            if (Similar(_candidate, text) >= 0.84)
                _candidateCount++;
            else
            {
                _candidate = text;
                _candidateCount = 1;
            }

            if (_candidateCount >= 1 && !_recentSentences.Any(previous => Similar(previous, text) >= 0.97))
                AcceptSubtitle(text, deferCoaching, loadReason);
        }
        else
        {
            if (Similar(_questCandidate, text) >= 0.84)
                _questCandidateCount++;
            else
            {
                _questCandidate = text;
                _questCandidateCount = 1;
            }

            if (_questCandidateCount >= 1 && !_recentQuests.Any(previous => Similar(previous, text) >= 0.97))
                AcceptQuest(text, deferCoaching, loadReason);
        }

        return true;
    }

    private void AcceptSubtitle(string text, bool deferCoaching = false, string loadReason = "")
    {
        if (_personalizationBusy)
        {
            CancelPersonalization();
            _nextPersonalizationAt = Environment.TickCount64 + 10_000;
        }
        _currentDialogue = text;
        _retryRequest = null;
        _recentSentences.Enqueue(text);
        while (_recentSentences.Count > 12)
            _recentSentences.Dequeue();

        _pendingDialogue = text;
        _ = TranslateVisibleAsync(text, false);
        if (deferCoaching)
            SetStatus($"{loadReason} · 已記住新對話，空閒時補充英文。");
    }

    private void AcceptQuest(string text, bool deferCoaching = false, string loadReason = "")
    {
        if (_personalizationBusy)
        {
            CancelPersonalization();
            _nextPersonalizationAt = Environment.TickCount64 + 10_000;
        }
        _retryRequest = null;
        _recentQuests.Enqueue(text);
        while (_recentQuests.Count > 12)
            _recentQuests.Dequeue();

        _pendingQuest = text;
        _currentQuest = text;
        if (Environment.TickCount64 - _lastDialogueAt > 4000)
            _ = TranslateVisibleAsync(text, true);
        if (deferCoaching)
            SetStatus($"{loadReason} · 已記住新任務，空閒時補充指引。");
    }

    private void ProcessDeferredLesson()
    {
        if (!string.IsNullOrWhiteSpace(_pendingQuest))
        {
            var quest = _pendingQuest;
            _pendingQuest = string.Empty;
            var includeTip = ShouldIncludeBuildTip();
            if (FastTranslationService.TryLocal(quest, true) is { } quick)
            {
                var reply = new CoachReply($"QUEST · {quest}", quest, $"現在要做：{quick}",
                    Array.Empty<KeywordCard>(), false);
                _readyReply = includeTip ? BuildAdvisor.AppendTip(reply, _config, quest, _buildGuides) : reply;
                return;
            }
            StartCoachRequest(
                $"QUEST · {quest}",
                    "正在準備任務指引與英文……",
                token => _coachService.GuideQuestAsync(
                    quest, _currentDialogue, _config, token, includeTip));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingDialogue))
        {
            var dialogue = _pendingDialogue;
            _pendingDialogue = string.Empty;
            StartCoachRequest(
                dialogue,
                "正在準備剛才的英文……",
                token => _coachService.ExplainAsync(dialogue, _config, token));
            return;
        }
        var retry = _retryRequest;
        _retryRequest = null;
        retry?.Invoke();
    }

    private async Task TranslateVisibleAsync(string text, bool quest)
    {
        var version = ++_translationVersion;
        _retryTranslation = null;
        _queuedTranslation = (text, quest, version); // One newest item, never a growing FIFO.
        _translationText = $"翻譯中 · {text}";
        if (_speakingCts is null) _chineseLabel.Text = _translationText;
        if (_translating) return;
        _translating = true;
        try
        {
            while (_queuedTranslation is { } next && _running && !IsDisposed)
            {
                _queuedTranslation = null;
                var result = _previewMode && _previewTranslate is not null
                    ? await _previewTranslate(next.Text, next.Quest)
                    : await _fastTranslation.TranslateAsync(next.Text, next.Quest, _config, _lifetimeCts.Token);
                if (!_running || IsDisposed || next.Version != _translationVersion) continue;
                _translationText = result.Text is { Length: > 0 }
                    ? result.Text : $"{result.Source} · {next.Text}";
                var retryLocal = _config.TranslationProvider == TranslationProviders.LocalOllama &&
                    result.Source.Contains("未啟動或超過", StringComparison.Ordinal);
                var retryAzure = _config.TranslationProvider == TranslationProviders.Azure &&
                    _config.OnlineTranslationEnabled && AzureCredentialStore.Exists();
                if (result.Text is null && (retryLocal || retryAzure))
                {
                    _retryTranslation = (next.Text, next.Quest);
                    _translationRetryAt = Environment.TickCount64 + 60_000;
                }
                if (_speakingCts is null) _chineseLabel.Text = _translationText;
                SetStatus(_config.TranslationProvider == TranslationProviders.Azure
                    ? $"翻譯：{result.Source} · {result.ElapsedMs} ms · 本次啟動送出 {_fastTranslation.CharactersUsed} 字元"
                    : $"翻譯：{result.Source} · {result.ElapsedMs} ms · 沒有上傳文字");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus($"翻譯暫停：{ex.Message}"); }
        finally
        {
            _translating = false;
            if (_queuedTranslation is { } waiting && _running && !IsDisposed)
            {
                _queuedTranslation = null;
                _ = TranslateVisibleAsync(waiting.Text, waiting.Quest);
            }
        }
    }

    private void TeachingTick()
    {
        if (!_running || _settingsOpen || _speakingCts is not null || _gameWindow is null ||
            NativeMethods.GetForegroundWindow() != _gameWindow.Handle)
        {
            if (_personalizationBusy) CancelPersonalization();
            return;
        }
        var now = Environment.TickCount64;
        var load = _loadMonitor.Sample(false);
        // Personalized generation is expendable background work. Stop it as soon
        // as action keys or high CPU indicate that the game needs the machine.
        if (_personalizationBusy && load.ShouldDefer)
        {
            CancelPersonalization();
            _nextPersonalizationAt = now + 10_000;
        }
        if (!_translating && now >= _translationRetryAt && _retryTranslation is { } retry &&
            (retry.Text == _currentDialogue || retry.Text == _currentQuest))
            _ = TranslateVisibleAsync(retry.Text, retry.Quest);
        var canSpeak = !load.ShouldDefer && NarrationMayStart() && now - _lastTeachingAt >= 1_000;
        if (canSpeak)
        {
            if (_readyReply is { } ready)
            {
                _readyReply = null;
                if (ready.Original == _currentDialogue || ready.Original == _currentQuest || ready.Original == $"QUEST · {_currentQuest}")
                    DisplayReply(ready);
            }
            if (!_speechService.IsBusy) TryIdleLesson(load);
            if (!_speechService.IsBusy && !_coachBusy) TrySpeaking(load);
        }

        if (_speakingCts is not null || _coachBusy || _readyReply is not null ||
            load.CpuPercent >= 95 || now < _narrationGuard.BlockedUntil)
            return;

        if (_pendingQuest.Length > 0 || _pendingDialogue.Length > 0 || _retryRequest is not null)
        {
            if (now < _nextModelLessonAt || _personalizationBusy)
                return;
            _config.InferenceThreads = AdaptiveLoadMonitor.InferenceBudget(load, Environment.ProcessorCount);
            // A complex current-screen judgment is allowed sooner while idle.
            // During action it keeps the conservative one-thread/120-second policy.
            _nextModelLessonAt = now + (load.ShouldDefer ? 120_000 : 40_000);
            ProcessDeferredLesson();
            return;
        }

        if (!_personalizationBusy && _idleLessons.NeedsPersonalizedLesson &&
            now >= _nextPersonalizationAt && load.ActionKeyIdleMs >= 8_000 && load.CpuPercent < 55)
        {
            _config.InferenceThreads = AdaptiveLoadMonitor.InferenceBudget(load, Environment.ProcessorCount);
            StartPersonalizedLesson();
        }
    }

    private void StartPersonalizedLesson()
    {
        if (_personalizationBusy || !_running || _settingsOpen)
            return;
        var learning = _idleLessons.CreatePersonalizationContext(_config);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _personalizationCts = cts;
        _personalizationBusy = true;
        _nextPersonalizationAt = Environment.TickCount64 + 30_000;
        _ = GeneratePersonalizedLessonAsync(learning, cts);
    }

    private async Task GeneratePersonalizedLessonAsync(
        LessonPersonalizationContext learning,
        CancellationTokenSource cts)
    {
        try
        {
            var lesson = await _coachService.CreatePersonalizedLessonAsync(
                learning, _currentQuest, _currentDialogue, _config, cts.Token);
            if (!cts.IsCancellationRequested && _running && lesson is not null &&
                _idleLessons.AddPersonalizedLesson(lesson))
                SetStatus($"已準備下一則{LearningTopicLabel(learning.Topic)}個人化教材 · 本機快取");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_personalizationCts, cts))
            {
                _personalizationBusy = false;
                _personalizationCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelPersonalization()
    {
        _personalizationCts?.Cancel();
        _personalizationCts = null;
        _personalizationBusy = false;
    }

    private bool NarrationEnvironmentClear() => _narrationGuard.MayStart(
        Environment.TickCount64, _running, _settingsOpen, _speakingCts is not null,
        _gameWindow is not null && NativeMethods.GetForegroundWindow() == _gameWindow.Handle);

    private bool NarrationMayStart() => !_speechService.IsBusy && NarrationEnvironmentClear();

    internal static string Narration(CoachReply reply)
    {
        var word = reply.Keywords.FirstOrDefault();
        return reply.TraditionalChinese +
            (word is null ? "" : $" 英文重點：{word.Word}，{word.Meaning}。") +
            (string.IsNullOrWhiteSpace(reply.Advice) ? "" : $" 配裝參考，不是背包判讀：{reply.Advice}");
    }

    private void TryIdleLesson(LoadSnapshot load)
    {
        var now = Environment.TickCount64;
        var safe = NarrationMayStart();
        var lesson = _idleLessons.TryNext(now, safe, _config, requireQuietWindow: false);
        if (lesson is null)
            return;
        // Keep the quest and its translation visible. Supplement only the right column.
        _keywordsLabel.Text = $"{lesson.Title}\n{lesson.English}\n{lesson.Chinese}";
        SetStatus(lesson.Title.StartsWith("AI 個人化", StringComparison.Ordinal)
            ? "空閒補充 · 個人化教材 · 已存本機避免重複"
            : "空閒補充 · 多益／數位 IC／遊戲英文備用教材");
        SpeakLesson(lesson.English, lesson.Chinese);
    }

    private void SpeakLesson(string english, string chinese)
    {
        _lastTeachingAt = Environment.TickCount64;
        if (_config.SpeakChinese)
            _speechService.SpeakTraditionalChinese(english.Length <= 120 && !chinese.Contains(english, StringComparison.OrdinalIgnoreCase)
                ? $"{english}。{chinese}" : chinese);
        else if (_config.SpeakEnglish)
            _speechService.SpeakEnglish(english);
    }

    private void DisplayReply(CoachReply reply)
    {
        if (_speechService.IsBusy)
        {
            _readyReply = reply;
            return;
        }
        _originalLabel.Text = reply.Original;
        _simpleLabel.Text = reply.SimpleEnglish;
        var words = reply.Keywords.Count == 0 ? "這句先不用背新單字" :
            string.Join("    ", reply.Keywords.Select(keyword => $"{keyword.Word}＝{keyword.Meaning}"));
        _keywordsLabel.Text = reply.Advice is null ? words : $"一般建議 · 非背包分析\n{reply.Advice}";
        SetStatus(reply.Notice ?? (reply.UsedLocalModel ? "任務與英文教學完成" : "本機基本教學"));
        _idleLessons.ObserveLesson(Environment.TickCount64, reply.Keywords, resetSchedule: false);
        SpeakLesson(reply.SimpleEnglish, Narration(reply));
    }

    private bool ShouldIncludeBuildTip()
    {
        if (!_config.BuildTipsEnabled)
            return false;
        _questGuidanceCount++;
        return _questGuidanceCount == 1 || _questGuidanceCount % 3 == 0;
    }

    private void StartCoachRequest(
        string original,
        string status,
        Func<CancellationToken, Task<CoachReply>> request)
    {
        if (!_running || _settingsOpen || IsDisposed || _coachBusy || _speakingCts is not null)
            return;
        CancelSpeaking();
        _retryRequest = () => StartCoachRequest(original, status, request);
        _originalLabel.Text = original;
        _simpleLabel.Text = "教練正在思考……";
        _keywordsLabel.Text = "正在找英文重點……";
        SetStatus(status);

        _translationCts?.Cancel();
        _translationCts?.Dispose();
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _translationCts = requestCts;
        _coachBusy = true;
        _ = ExplainAndDisplayAsync(request(requestCts.Token), requestCts);
    }

    private async Task ExplainAndDisplayAsync(
        Task<CoachReply> pendingReply,
        CancellationTokenSource requestCts)
    {
        var cancellationToken = requestCts.Token;
        try
        {
            var reply = await pendingReply;
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!_running || IsDisposed)
                return;
            _retryRequest = null;
            // Do not speak a finished result over newly started combat/dialogue.
            _readyReply = reply; // The teaching timer owns cadence, not request completion.
        }
        catch (OperationCanceledException)
        {
            // A newer subtitle replaced this one.
        }
        catch (Exception exception)
        {
            SetStatus($"教練暫時無法回答：{exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_translationCts, requestCts))
                _coachBusy = false;
        }
    }

    private void ShowSettingsMenu()
    {
        if (_settingsMenu?.Visible == true)
            return;
        _settingsMenu?.Dispose();
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.FromArgb(30, 33, 42),
            ForeColor = Color.WhiteSmoke,
            Font = Font
        };
        _settingsMenu = menu;
        _settingsOpen = true;
        _runVersion++;
        CancelSpeaking();
        CancelPersonalization();
        _translationCts?.Cancel();
        // Retain the menu until the next opening/form shutdown. Disposing in
        // Closed breaks ToolStrip's own click/close event sequence.
        menu.Closed += (_, _) =>
        {
            _settingsOpen = false;
            _idleLessons.Reset(Environment.TickCount64);
            _speakingPlanner.Reset(Environment.TickCount64);
        };
        var recognition = new ToolStripMenuItem("辨識範圍");
        recognition.DropDownItems.Add("設定對話字幕區", null, (_, _) => PickRegion(CaptureRegionKind.Dialogue));
        recognition.DropDownItems.Add("設定任務目標區", null, (_, _) => PickRegion(CaptureRegionKind.Quest));
        recognition.DropDownItems.Add("測試兩個辨識區", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(recognition);

        menu.Items.Add("聲音與語速", null, (_, _) => OpenVoiceSettings());
        menu.Items.Add($"翻譯方式（{(_config.TranslationProvider == TranslationProviders.Azure ? "Azure" : "本機")}）",
            null, (_, _) => OpenTranslationSettings());
        menu.Items.Add("本機流派資料／更新狀態", null, (_, _) => ShowBuildGuideDetails());
        menu.Items.Add("完整翻譯／運作狀態（精簡語音版）", null, (_, _) =>
            MessageBox.Show(this, $"{_translationText}\n\n{_statusLabel.Text}\n\n教學：{_keywordsLabel.Text}", "精簡語音版"));
        var speaking = new ToolStripMenuItem("自動口說（空閒時）") { Checked = _config.AutoSpeakingEnabled };
        speaking.Click += (_, _) => ToggleAutoSpeaking();
        menu.Items.Add(speaking);

        var learning = new ToolStripMenuItem($"教學重點：{LearningFocusLabel(_config.LearningFocus)}");
        AddChoiceItems(learning, new (string Label, string Value)[]
        {
            ("綜合（多益＋數位 IC＋遊戲）", LearningFocusOptions.Balanced),
            ("多益優先", LearningFocusOptions.ToeicFirst),
            ("數位 IC 面試優先", LearningFocusOptions.DigitalIcFirst)
        }, _config.LearningFocus, value => _config.LearningFocus = value);
        menu.Items.Add(learning);

        var opacity = new ToolStripMenuItem("透明度");
        foreach (var value in new[] { 0.65, 0.80, 0.92 })
        {
            var item = new ToolStripMenuItem($"{value:P0}") { Checked = Math.Abs(Opacity - value) < 0.02 };
            item.Click += (_, _) => SetOverlayOpacity(value);
            opacity.DropDownItems.Add(item);
        }
        menu.Items.Add(opacity);
        var translationWidth = new ToolStripMenuItem("翻譯列寬度");
        foreach (var width in new[] { 740, 960, 1200 })
        {
            var choice = new ToolStripMenuItem($"{width} 像素") { Checked = Math.Abs(Width - width) < 5 };
            choice.Click += (_, _) => SetTranslationWidth(width);
            translationWidth.DropDownItems.Add(choice);
        }
        menu.Items.Add(translationWidth);

        menu.Items.Add("移到遊戲下方中央", null, (_, _) => MoveToSafePosition());
        menu.Items.Add(new ToolStripSeparator());

        var tipsEnabled = new ToolStripMenuItem("自動提供流派／裝備提示")
        {
            Checked = _config.BuildTipsEnabled,
            CheckOnClick = true
        };
        tipsEnabled.CheckedChanged += (_, _) =>
        {
            _config.BuildTipsEnabled = tipsEnabled.Checked;
            _config.Save();
        };
        menu.Items.Add(tipsEnabled);

        var classMenu = new ToolStripMenuItem($"職業：{ClassLabel(_config.PlayerClass)}");
        AddChoiceItems(classMenu, new (string Label, string Value)[]
        {
            ("死靈法師", "Necromancer"), ("野蠻人", "Barbarian"), ("聖教軍", "Crusader"),
            ("狩魔獵人", "DemonHunter"), ("武僧", "Monk"), ("秘術師", "Wizard"),
            ("血騎士", "BloodKnight"), ("霧刃", "Tempest"), ("德魯伊", "Druid")
        }, _config.PlayerClass, value => _config.PlayerClass = value);
        menu.Items.Add(classMenu);

        var styleMenu = new ToolStripMenuItem($"偏好：{StyleLabel(_config.BuildPreference)}");
        AddChoiceItems(styleMenu, new (string Label, string Value)[]
        {
            ("輕鬆推進", "EasyPvE"), ("召喚流", "Summons"),
            ("傷害優先", "Damage"), ("生存優先", "Survival")
        }, _config.BuildPreference, value => _config.BuildPreference = value);
        menu.Items.Add(styleMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束程式", null, (_, _) => Close());

        menu.Show(_settingsButton, new Point(0, _settingsButton.Height));
    }

    private async Task UpdateBuildGuidesAsync()
    {
        await _buildGuides.RefreshAsync(_lifetimeCts.Token);
        // Do not overwrite the active lesson/status or start extra narration.
    }

    private void ShowBuildGuideDetails()
    {
        _settingsOpen = true;
        var guide = _buildGuides.Current;
        try
        {
            MessageBox.Show(this,
                $"{_buildGuides.Status}\n{_buildGuides.LastResult}\n\n" +
                $"範圍：死靈法師 PvE 來源候選，不是個人背包分析。\n" +
                $"主要攻擊：{guide.PrimaryAttack}\n技能：{string.Join("、", guide.Skills)}\n" +
                $"套裝候選：{guide.SetName}\n武器候選：{guide.MainWeapon}／{guide.OffHand}\n\n" +
                "前期不必照抄完整配置；先確認技能解鎖、裝備持有與遊戲實際效果。\n" +
                "啟動時背景檢查此来源，最多等 8 秒；失敗沿用快取。來源有更新不等於已驗證當季最強。\n\n" +
                $"來源：{guide.SourceUrl}", "本機流派參考", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { _settingsOpen = false; RestoreGameFocus(); }
    }

    private void ToggleAutoSpeaking()
    {
        CancelSpeaking();
        _settingsOpen = true;
        try
        {
            if (_config.AutoSpeakingEnabled)
                _config.AutoSpeakingEnabled = false;
            else
            {
                if (!SpeakingRecognitionService.ModelAvailable)
                {
                    MessageBox.Show(this, "缺少英文口說模型。請執行專案的「安裝口說模型.ps1」，再重新啟動教練。",
                        "口說尚未就緒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var consent = MessageBox.Show(this,
                    "啟用後，每隔至少 4 分鐘、確認空閒才邀請跟讀。\n\n" +
                    "教練念完後會顯示「麥克風開啟」，使用 Windows 預設麥克風最多 10 秒；" +
                    "5 秒無聲會跳過，說完安靜約 1.2 秒即停止。\n\n" +
                    "錄音僅在記憶體中由 Vosk 本機辨識，不存檔、不上傳、不常駐監聽。" +
                    "示範聲音沿用語音設定（自然女聲會傳送示範文字給 Microsoft，不傳你的錄音）。\n\n" +
                    "按鍵、切離遊戲或開設定會取消；滑鼠／手把戰鬥可能漏判。建議戴耳機，" +
                    "避免遊戲聲音被誤認。辨識文字不是發音評分。\n\n是否同意並啟用？",
                    "啟用自動口說與麥克風", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (consent != DialogResult.Yes) return;
                _config.AutoSpeakingEnabled = true;
            }
            _config.Save();
            _speakingFaulted = false;
            _speakingPlanner.Reset(Environment.TickCount64);
            SetStatus(_config.AutoSpeakingEnabled ? "自動口說已啟用 · 空閒時約每 4 分鐘邀請一次" : "自動口說已關閉 · 麥克風關閉");
        }
        finally { _settingsOpen = false; RestoreGameFocus(); }
    }

    private bool TrySpeaking(LoadSnapshot load)
    {
        var now = Environment.TickCount64;
        var safe = SpeakingPlanner.CanStart(_config.AutoSpeakingEnabled && !_previewMode && !_speakingFaulted,
            _gameWindow is not null && NativeMethods.GetForegroundWindow() == _gameWindow.Handle,
            !_running || _coachBusy || _settingsOpen || _speechService.IsBusy || _speakingCts is not null ||
            _pendingQuest.Length > 0 || _pendingDialogue.Length > 0 || _readyReply is not null ||
            now - _lastTeachingAt < 15_000,
            load.CpuPercent, load.ActionKeyIdleMs, _loadMonitor.DialogueKeyIdleMs, now - _lastDialogueAt);
        var prompt = _speakingPlanner.TryNext(now, _config.AutoSpeakingEnabled, safe);
        if (prompt is null) return false;
        if (!SpeakingRecognitionService.ModelAvailable)
        {
            _speakingFaulted = true;
            SetStatus("口說模型缺少；請重新安裝 speech-model。麥克風未開啟。");
            return false;
        }
        // A tiny visual sentinel, not OCR, conservatively cancels when the subtitle
        // region changes. This can also cancel while travelling; safety wins.
        _speakingScreen = GetDialogueFingerprint();
        _lastSpeakingScreenCheck = now;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _speakingCts = cts;
        _speakingGuard.Start();
        _ = RunSpeakingAsync(prompt, cts);
        return true;
    }

    private async Task RunSpeakingAsync(SpeakingPrompt prompt, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            _speakingDemonstration = true;
            _skipSpeakingResponse = false;
            _keywordsLabel.Text = $"口說示範 · 麥克風關閉\n{prompt.English}\n{prompt.Chinese}（練習句，非任務指令）";
            SetStatus("先聽一句 · 念完才收音；不回答就略過");
            var text = await SpeakingSession.RunAsync(
                () => _speechService.SpeakEnglishAndWaitAsync(prompt.English),
                SpeakingRecognitionService.RecordAsync, SpeakingRecognitionService.RecognizeAsync,
                listening =>
                {
                    CheckSpeakingSafety();
                    if (listening)
                    {
                        _speakingDemonstration = false;
                        if (_skipSpeakingResponse) CancelSpeaking();
                    }
                    token.ThrowIfCancellationRequested();
                    _keywordsLabel.Text = listening
                        ? $"🎤 麥克風開啟 · 換你說\n{prompt.English}\n{prompt.Chinese} · 不說話就跳過"
                        : $"麥克風已關閉 · 本機核對中\n{prompt.English}";
                    SetStatus(listening ? "🎤 正在收音（最多 10 秒）· 按鍵／離開遊戲可中止"
                        : "麥克風已關閉 · OCR／翻譯仍暫停，避免搶 CPU");
                    _chineseLabel.Text = listening ? $"🎤 換你說：{prompt.English}（最多 10 秒）" : _translationText;
                }, token);
            token.ThrowIfCancellationRequested();
            CheckSpeakingSafety();
            token.ThrowIfCancellationRequested();
            var heard = text.Length > 85 ? text[..85] + "…" : text;
            _keywordsLabel.Text = text.Length == 0 ? SpeakingFeedback.Describe(prompt.English, text)
                : $"聽到：{heard}\n{SpeakingFeedback.Describe(prompt.English, text)}";
            SetStatus("口說完成 · 麥克風關閉 · 文字核對非發音評分");
            // The compact overlay has no teaching column; feedback is spoken once
            // after recording/decode finished and the microphone has closed.
            SpeakLesson(prompt.English, SpeakingFeedback.Describe(prompt.English, text));
        }
        catch (OperationCanceledException) { /* No late feedback after cancellation. */ }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                _speakingFaulted = true; // Do not repeat device/permission errors every four minutes.
                _keywordsLabel.Text = "口說暫停，麥克風已關閉。請檢查預設麥克風／桌面應用程式麥克風權限，再關閉並重新開啟教練。";
                SetStatus($"口說暫停：{exception.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_speakingCts, cts))
            {
                _speakingDemonstration = false;
                _speakingGuard.Stop();
                _speakingCts = null;
                _chineseLabel.Text = _translationText;
                _speakingScreen = null;
                _lastTeachingAt = Environment.TickCount64;
                _idleLessons.Reset(_lastTeachingAt);
                _speakingPlanner.Reset(_lastTeachingAt);
                if (!IsDisposed && !Disposing && _keywordsLabel.Text.StartsWith("口說已中止"))
                    _keywordsLabel.Text = "口說已中止 · 麥克風已關閉\n先繼續遊戲，之後空閒再練。";
            }
            cts.Dispose();
        }
    }

    private void CancelSpeaking()
    {
        if (_speakingCts is null || _speakingCts.IsCancellationRequested) return;
        _speakingCts.Cancel();
        _speakingGuard.Stop();
        if (!IsDisposed && !Disposing)
        {
            _keywordsLabel.Text = "口說已中止 · 正在關閉麥克風\n先繼續遊戲，之後空閒再練。";
            SetStatus("口說已取消 · 正在釋放收音／辨識資源");
        }
        // Keep _speakingCts until cleanup finishes: no overlapping OCR/model work.
    }

    private void CheckSpeakingSafety()
    {
        if (_speakingCts is null || _speakingCts.IsCancellationRequested) return;
        var load = _loadMonitor.Sample(false);
        if (SpeakingPlanner.ShouldInterrupt(_running && _config.AutoSpeakingEnabled,
            _gameWindow is not null && NativeMethods.GetForegroundWindow() == _gameWindow.Handle,
            _settingsOpen, load.CpuPercent, load.ActionKeyIdleMs, _loadMonitor.DialogueKeyIdleMs))
        {
            DeferOrCancelSpeaking();
            return;
        }
        if (Environment.TickCount64 - _lastSpeakingScreenCheck < 1000) return;
        _lastSpeakingScreenCheck = Environment.TickCount64;
        try
        {
            var current = GetDialogueFingerprint();
            if (_speakingScreen is null || current.Zip(_speakingScreen, (a, b) => Math.Abs(a - b)).Average() > 18)
                DeferOrCancelSpeaking();
        }
        catch { DeferOrCancelSpeaking(); }
    }

    private void DeferOrCancelSpeaking()
    {
        // Activity while demonstrating means: finish the sentence, then skip the
        // microphone. While listening/decoding, cancel immediately for privacy/load.
        if (_speakingDemonstration)
            _skipSpeakingResponse = true;
        else
            CancelSpeaking();
    }

    private byte[] GetDialogueFingerprint()
    {
        using var capture = CaptureService.CaptureRegion(_gameWindow!, _config, CaptureRegionKind.Dialogue);
        using var small = new Bitmap(capture, new Size(64, 16));
        var result = new byte[64 * 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 64; x++)
            {
                var pixel = small.GetPixel(x, y);
                result[y * 64 + x] = (byte)((pixel.R + pixel.G + pixel.B) / 3);
            }
        return result;
    }

    private void AddChoiceItems(
        ToolStripMenuItem parent,
        IEnumerable<(string Label, string Value)> choices,
        string selected,
        Action<string> apply)
    {
        foreach (var choice in choices)
        {
            var item = new ToolStripMenuItem(choice.Label) { Checked = choice.Value == selected };
            item.Click += (_, _) =>
            {
                apply(choice.Value);
                _config.Save();
                SetStatus($"設定已更新：{choice.Label}");
            };
            parent.DropDownItems.Add(item);
        }
    }

    private void PickRegion(CaptureRegionKind kind)
    {
        _gameWindow ??= CaptureService.FindDiabloWindow();
        if (_gameWindow is null)
        {
            MessageBox.Show("請先開啟 Diablo Immortal，再設定辨識區域。", "找不到遊戲", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var wasRunning = _running;
        _settingsOpen = true;
        _runVersion++;
        _scanTimer.Stop();
        Hide();
        try
        {
            Thread.Sleep(180);
            if (!CaptureService.TryRefresh(_gameWindow, out var refreshed))
                throw new InvalidOperationException("遊戲視窗目前不可見，或已最小化。");
            _gameWindow = refreshed;
            using var screenshot = CaptureService.CaptureClient(_gameWindow);
            using var picker = new RegionPickerForm(screenshot, _config, kind, _gameWindow.ClientBounds);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                var selection = picker.SelectedImageRectangle;
                if (kind == CaptureRegionKind.Dialogue)
                {
                    _config.RegionX = selection.X / (double)screenshot.Width;
                    _config.RegionY = selection.Y / (double)screenshot.Height;
                    _config.RegionWidth = selection.Width / (double)screenshot.Width;
                    _config.RegionHeight = selection.Height / (double)screenshot.Height;
                    _candidate = string.Empty;
                    _candidateCount = 0;
                }
                else
                {
                    _config.QuestRegionX = selection.X / (double)screenshot.Width;
                    _config.QuestRegionY = selection.Y / (double)screenshot.Height;
                    _config.QuestRegionWidth = selection.Width / (double)screenshot.Width;
                    _config.QuestRegionHeight = selection.Height / (double)screenshot.Height;
                    _config.QuestRegionConfigured = true;
                    _questCandidate = string.Empty;
                    _questCandidateCount = 0;
                }
                _config.Save();
                SetStatus(kind == CaptureRegionKind.Dialogue
                    ? "對話字幕區已儲存。"
                    : "任務目標區已儲存；教練現在能主動說明下一步。");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "無法框選", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _settingsOpen = false;
            Show();
            RestoreGameFocus();
            if (wasRunning)
                _scanTimer.Start();
        }
    }

    private async Task TestCaptureAsync()
    {
        var wasRunning = _running;
        _scanTimer.Stop();
        await ScanOnceAsync(force: true);
        if (wasRunning)
            _scanTimer.Start();
    }

    private void OpenVoiceSettings()
    {
        _settingsOpen = true;
        using var settings = new VoiceSettingsForm(_config, _speechService);
        try { settings.ShowDialog(this); }
        finally { _settingsOpen = false; }
        RestoreGameFocus();
    }

    private void OpenTranslationSettings()
    {
        _settingsOpen = true;
        _translationVersion++;
        _queuedTranslation = null;
        _retryTranslation = null;
        _fastTranslation.Cancel();
        using var settings = new TranslationSettingsForm(_config);
        try { settings.ShowDialog(this); }
        finally { _settingsOpen = false; }
        RestoreGameFocus();
    }

    private void SetOverlayOpacity(double value)
    {
        Opacity = value;
        _config.WindowOpacity = value;
        _config.Save();
    }

    private void MoveToSafePosition()
    {
        _gameWindow ??= CaptureService.FindDiabloWindow();
        if (_gameWindow is not null && CaptureService.TryRefresh(_gameWindow, out var refreshed))
        {
            _gameWindow = refreshed;
            PositionOverGameWindow(refreshed.ClientBounds);
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            _config.WindowBottom = Bottom;
            _config.Save();
        }
        RestoreGameFocus();
    }

    private static string ClassLabel(string value) => value switch
    {
        "Necromancer" => "死靈法師", "Barbarian" => "野蠻人", "Crusader" => "聖教軍",
        "DemonHunter" => "狩魔獵人", "Monk" => "武僧", "Wizard" => "秘術師",
        "BloodKnight" => "血騎士", "Tempest" => "霧刃", "Druid" => "德魯伊",
        _ => value
    };

    private static string StyleLabel(string value) => value switch
    {
        "Summons" => "召喚流", "Damage" => "傷害優先", "Survival" => "生存優先",
        _ => "輕鬆推進"
    };

    private static string LearningFocusLabel(string value) => value switch
    {
        LearningFocusOptions.ToeicFirst => "多益優先",
        LearningFocusOptions.DigitalIcFirst => "數位 IC 優先",
        _ => "綜合"
    };

    private static string LearningTopicLabel(string value) => value switch
    {
        "toeic" => "多益",
        "ic" => "數位 IC",
        _ => "遊戲英文"
    };

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _running = false;
        CancelSpeaking();
        _speakingGuard.Stop();
        _runVersion++;
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        _config.WindowBottom = Bottom;
        _config.Save();
        _scanTimer.Stop();
        _lifetimeCts.Cancel();
        _teachingTimer.Stop();
        CancelPersonalization();
        _fastTranslation.Dispose();
        _translationCts?.Cancel();
        _speechService.Dispose();
        _loadMonitor.Dispose();
        _settingsMenu?.Dispose();
        _translationCts?.Dispose();
        _lifetimeCts.Dispose();
    }

    private void SetStatus(string text)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
        {
            if (IsHandleCreated)
            {
                try { BeginInvoke(() => SetStatus(text)); }
                catch (InvalidOperationException) { /* Window closed between check and dispatch. */ }
            }
            return;
        }
        _statusLabel.Text = $"OCR：{_ocrService.RecognizerLanguage} · {text}";
    }

    internal bool VerifyUiAndLoadDemo()
    {
        if (!_previewMode)
            throw new InvalidOperationException("UI regression is preview-only.");
        static IEnumerable<Control> Descendants(Control control) =>
            control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
        var buttons = Descendants(this).OfType<Button>().Select(button => button.Text).ToArray();
        if (!buttons.SequenceEqual(new[] { "開啟", "設定" }) || Height > 80)
            return false;
        for (var i = 0; i < 8; i++)
        {
            ShowSettingsMenu();
            Application.DoEvents();
            if (!_settingsMenu!.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "自動口說（空閒時）"))
                return false;
            if (!_settingsMenu.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "本機流派資料／更新狀態"))
                return false;
            if (!_settingsMenu.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "翻譯方式（本機）"))
                return false;
            if (!_settingsMenu!.Items.OfType<ToolStripMenuItem>().Any(item => item.Text?.StartsWith("教學重點：", StringComparison.Ordinal) == true))
                return false;
            _settingsMenu!.Close();
            Application.DoEvents();
            if (_settingsOpen || _settingsMenu.IsDisposed)
                return false;
        }
        using (var translationSettings = new TranslationSettingsForm(new CoachConfig()))
        {
            var radios = Descendants(translationSettings).OfType<RadioButton>().ToArray();
            if (!radios.Any(item => item.Checked && item.Text.Contains("本機快速翻譯")) ||
                !radios.Any(item => item.Text.Contains("Azure Translator")))
                return false;
        }
        ToggleRunning();
        var pending = new TaskCompletionSource<CoachReply>();
        StartCoachRequest("test", "test", _ => pending.Task);
        var request = _translationCts!;
        ToggleRunning();
        if (_running || _scanTimer.Enabled || !request.IsCancellationRequested || _retryRequest is not null)
            return false;
        pending.SetResult(new CoachReply("late", "late", "late", Array.Empty<KeywordCard>(), true));
        Application.DoEvents();
        if (_originalLabel.Text == "late")
            return false;
        // OCR may change while HTTP is pending: only newest queued text survives.
        _running = true;
        var oldTranslation = new TaskCompletionSource<TranslationResult>();
        var newTranslation = new TaskCompletionSource<TranslationResult>();
        var translatedInputs = new List<string>();
        _previewTranslate = (text, _) =>
        {
            translatedInputs.Add(text);
            return translatedInputs.Count == 1 ? oldTranslation.Task : newTranslation.Task;
        };
        _ = TranslateVisibleAsync("old sentence", false);
        _ = TranslateVisibleAsync("middle sentence", false);
        _ = TranslateVisibleAsync("new sentence", false);
        oldTranslation.SetResult(new("過期譯文", "test", 0));
        Application.DoEvents();
        if (_chineseLabel.Text == "過期譯文" || !translatedInputs.SequenceEqual(new[] { "old sentence", "new sentence" })) return false;
        newTranslation.SetResult(new("最新譯文", "test", 0));
        Application.DoEvents();
        if (_chineseLabel.Text != "最新譯文") return false;
        var stoppedTranslation = new TaskCompletionSource<TranslationResult>();
        _previewTranslate = (_, _) => stoppedTranslation.Task;
        _ = TranslateVisibleAsync("stop sentence", false);
        ToggleRunning();
        stoppedTranslation.SetResult(new("關閉後不應顯示", "test", 0));
        Application.DoEvents();
        if (_chineseLabel.Text == "關閉後不應顯示" || _teachingTimer.Enabled) return false;
        _previewTranslate = null;
        // UI cancellation regression with no audio device or native recognizer.
        using (var fakeSpeaking = new CancellationTokenSource())
        {
            _speakingCts = fakeSpeaking;
            _speakingDemonstration = true;
            _skipSpeakingResponse = false;
            DeferOrCancelSpeaking();
            if (fakeSpeaking.IsCancellationRequested || !_skipSpeakingResponse) return false;
            _speakingDemonstration = false;
            DeferOrCancelSpeaking();
            if (!fakeSpeaking.IsCancellationRequested) return false;
            _speakingCts = null;
        }
        using (var fakeSpeaking = new CancellationTokenSource())
        {
            _speakingCts = fakeSpeaking;
            ShowSettingsMenu();
            Application.DoEvents();
            if (!fakeSpeaking.IsCancellationRequested) return false;
            _settingsMenu!.Close();
            _speakingCts = null;
        }
        _originalLabel.Text = "QUEST · Head to Guard's Watch";
        _simpleLabel.Text = "Go to Guard's Watch.";
        // Long translations must remain fully accessible without growing into a
        // large game-blocking panel. New text resets any old scroll position.
        var shortHeight = Height;
        var bottom = Bottom;
        _chineseLabel.Text = string.Join("\n", Enumerable.Range(1, 12).Select(n => $"第 {n} 行：這是一段很長的翻譯，全部內容都可以捲動閱讀。"));
        Application.DoEvents();
        if (_chineseLabel.AutoEllipsis || !_translationViewport.VerticalScroll.Visible ||
            _chineseLabel.Height <= _translationViewport.Height || Height <= shortHeight || Math.Abs(Bottom - bottom) > 2)
            return false;
        _translationViewport.AutoScrollPosition = new Point(0, 10000);
        if (_translationViewport.AutoScrollPosition.Y >= 0 || _chineseLabel.Bottom > _translationViewport.ClientSize.Height + 1) return false;
        _chineseLabel.Text = "短句。";
        Application.DoEvents();
        if (_translationViewport.AutoScrollPosition.Y != 0 || _translationViewport.VerticalScroll.Visible || Height != shortHeight)
            return false;
        _chineseLabel.Text = "翻譯示範：這是一段比較長的對話。新的翻譯列會自動換行，不會只顯示前半句；內容更多時，視窗最多展開四行，其餘文字可用右側捲軸閱讀。\n短句會自動縮回，英文教學與攻略仍然只用語音，不增加其他面板。";
        _keywordsLabel.Text = "口說示範 · 麥克風關閉\nWhere should I go next?\n我接下來該去哪裡？（練習句）";
        SetStatus("預覽 · 任務優先，空閒時邀請口說 · 此預覽不收音");
        return true;
    }

    private void DragOverlay(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
            return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WmNcLeftButtonDown, NativeMethods.HtCaption, nint.Zero);
        ClampToVisibleScreen();
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        _config.WindowBottom = Bottom;
        _config.Save();
    }

    private void PositionOverGameWindow(Rectangle gameBounds)
    {
        Location = new Point(
            gameBounds.Left + Math.Max(0, (gameBounds.Width - Width) / 2),
            Math.Max(gameBounds.Top, gameBounds.Bottom - Height - 10));
        ClampToVisibleScreen();
    }

    private void RestoreGameFocus()
    {
        if (!IsDisposed && !Disposing && _gameWindow is not null && _gameWindow.Handle != nint.Zero)
            NativeMethods.SetForegroundWindow(_gameWindow.Handle);
    }

    private bool SavedLocationOverlapsEnemyHud(Rectangle screenArea)
    {
        if (_config.WindowLeft < 0 || _config.WindowTop < 0)
            return false;
        var saved = new Rectangle(_config.WindowLeft, _config.WindowTop, Width, Height);
        return OverlapsEnemyHud(saved, screenArea);
    }

    internal static bool OverlapsEnemyHud(Rectangle overlay, Rectangle screenArea)
    {
        var enemyHud = new Rectangle(
            screenArea.Left + (int)Math.Round(screenArea.Width * 0.31),
            screenArea.Top,
            (int)Math.Round(screenArea.Width * 0.38),
            Math.Min(150, screenArea.Height));
        return overlay.IntersectsWith(enemyHud);
    }

    private bool SavedLocationIsVisible(Rectangle screenArea)
    {
        if (_config.WindowLeft < 0 || _config.WindowTop < 0)
            return false;
        var saved = new Rectangle(_config.WindowLeft, _config.WindowTop, Width, CompactHeight);
        return Rectangle.Intersect(saved, screenArea).Width >= 200;
    }

    private void ClampToVisibleScreen()
    {
        var screen = Screen.FromRectangle(Bounds).WorkingArea;
        Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - Width));
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
    }

    private void ApplyRoundedCorners()
    {
        if (Width <= 0 || Height <= 0 || !IsHandleCreated)
            return;
        var handle = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 18, 18);
        if (handle == nint.Zero)
            return;
        var previous = Region;
        Region = Region.FromHrgn(handle);
        NativeMethods.DeleteObject(handle);
        previous?.Dispose();
    }

    private static double Similar(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return 0;
        first = NormalizeForComparison(first);
        second = NormalizeForComparison(second);
        if (first == second)
            return 1;

        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var i = 1; i <= first.Length; i++)
        {
            var current = new int[second.Length + 1];
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                var cost = first[i - 1] == second[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            previous = current;
        }

        return 1 - previous[^1] / (double)Math.Max(first.Length, second.Length);
    }

    private static string NormalizeForComparison(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }
        return builder.ToString();
    }

    private static void ConfigureCompactButton(Button button, string text, string tip, EventHandler onClick, ToolTip toolTip)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Height = 25;
        button.Width = text is "開啟" or "關閉" or "設定"
            ? 48
            : text.Length <= 2 ? 34 : text.Length <= 4 ? 46 : 55;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.FromArgb(38, 42, 52);
        button.ForeColor = Color.WhiteSmoke;
        button.Font = new Font("Microsoft JhengHei UI", 8, FontStyle.Regular);
        button.Padding = new Padding(1, 0, 1, 0);
        button.Margin = new Padding(2, 0, 0, 0);
        button.Click += onClick;
        toolTip.SetToolTip(button, tip);
    }

    private static void ConfigureContentLabel(Label label, string text, Color color, float size, FontStyle style)
    {
        label.Text = text;
        label.ForeColor = color;
        label.Font = new Font("Microsoft JhengHei UI", size, style);
        label.Dock = DockStyle.Fill;
        label.AutoEllipsis = true;
        label.Padding = new Padding(5, 1, 5, 2);
    }

    private static Control WrapSection(string heading, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(2),
            Padding = new Padding(3, 1, 3, 2),
            BackColor = Color.FromArgb(30, 33, 42)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = heading,
            AutoSize = true,
            ForeColor = Color.FromArgb(156, 163, 175),
            Font = new Font("Microsoft JhengHei UI", 8, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0)
        }, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }
}
