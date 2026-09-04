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
    private ContextMenuStrip? _settingsMenu;
    private bool _settingsOpen;
    private int _runVersion;
    private long _lastDialogueAt;
    private CoachReply? _readyReply;
    private Action? _retryRequest;
    private bool _speechActive;
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
    private readonly Label _keywordsLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _settingsButton = new();
    private readonly Panel _contentPanel = new();
    private const int ExpandedHeight = 158;
    private const int CompactHeight = 38;

    public CoachForm(bool previewMode = false)
    {
        _previewMode = previewMode;
        _ocrService = new OcrService();
        _speechService = new SpeechService(_config, initializeLocalVoice: !previewMode);
        _speechService.StatusChanged += message => SetStatus(message);
        InitializeUi();
        _scanTimer.Interval = Math.Clamp(_config.ScanIntervalMs, 700, 5000);
        _scanTimer.Tick += ScanTimerTick;
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
        var overlayWidth = Math.Clamp((int)Math.Round(area.Width * 0.64), 780, 1280);
        Size = new Size(overlayWidth, ExpandedHeight);
        MinimumSize = new Size(700, CompactHeight);
        MaximumSize = new Size(1600, ExpandedHeight);
        var defaultLocation = new Point(
            area.Left + (area.Width - Width) / 2,
            Math.Max(area.Top, area.Bottom - Height - 10));
        var useSavedLocation = SavedLocationIsVisible(area) && !SavedLocationOverlapsEnemyHud(area);
        _positionAtGameOnShown = !useSavedLocation;
        Location = useSavedLocation
            ? new Point(_config.WindowLeft, _config.WindowTop)
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
            RowCount = 2,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = BackColor
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(topBar, 0, 0);

        var title = new Label
        {
            Text = "◆ ENGLISH COACH",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 205, 76),
            Margin = new Padding(2, 0, 4, 0),
            Cursor = Cursors.SizeAll
        };
        title.MouseDown += DragOverlay;
        topBar.Controls.Add(title, 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Color.FromArgb(156, 163, 175);
        _statusLabel.Text = $"OCR：{_ocrService.RecognizerLanguage} · 未啟動";
        _statusLabel.Margin = new Padding(0);
        _statusLabel.Cursor = Cursors.SizeAll;
        _statusLabel.MouseDown += DragOverlay;
        topBar.Controls.Add(_statusLabel, 1, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 0, 0, 0)
        };
        var tips = new ToolTip { InitialDelay = 250, ReshowDelay = 100 };
        ConfigureCompactButton(_startButton, "開啟", "開啟／關閉自動教練", (_, _) => ToggleRunning(), tips);
        ConfigureCompactButton(_settingsButton, "設定", "辨識、聲音、顯示與流派設定", (_, _) => ShowSettingsMenu(), tips);
        var closeButton = new Button();
        ConfigureCompactButton(closeButton, "×", "關閉教練", (_, _) => Close(), tips);
        closeButton.ForeColor = Color.FromArgb(248, 113, 113);
        toolbar.Controls.AddRange(new Control[]
        {
            _startButton, _settingsButton, closeButton
        });
        topBar.Controls.Add(toolbar, 2, 0);

        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Margin = new Padding(0, 2, 0, 0);
        _contentPanel.Visible = true;
        root.Controls.Add(_contentPanel, 0, 1);

        var contentGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = BackColor
        };
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        _contentPanel.Controls.Add(contentGrid);

        ConfigureContentLabel(_originalLabel, "等待英文字幕……", Color.WhiteSmoke, 10, FontStyle.Bold);
        ConfigureContentLabel(_simpleLabel, "簡單英文會顯示在這裡", Color.FromArgb(147, 197, 253), 9, FontStyle.Regular);
        ConfigureContentLabel(_chineseLabel, "繁體中文會顯示在這裡", Color.FromArgb(167, 243, 208), 10, FontStyle.Regular);
        ConfigureContentLabel(_keywordsLabel, "重點單字會顯示在這裡", Color.FromArgb(253, 230, 138), 9, FontStyle.Regular);

        var englishStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 4, 0) };
        englishStack.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        englishStack.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        englishStack.Controls.Add(WrapSection("ENGLISH", _originalLabel), 0, 0);
        englishStack.Controls.Add(WrapSection("SIMPLE", _simpleLabel), 0, 1);
        contentGrid.Controls.Add(englishStack, 0, 0);
        contentGrid.Controls.Add(WrapSection("繁中 · 不爆雷", _chineseLabel), 1, 0);
        contentGrid.Controls.Add(WrapSection("WORDS · 空閒教學", _keywordsLabel), 2, 0);

        ApplyRoundedCorners();
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        _gameWindow = CaptureService.FindDiabloWindow();
        if (_gameWindow is not null && _positionAtGameOnShown)
            PositionOverGameWindow(_gameWindow.ClientBounds);
        var model = await _coachService.CheckAsync(_config, _lifetimeCts.Token);
        var game = _gameWindow is null ? "尚未找到 Diablo Immortal 視窗" : "已找到遊戲視窗";
        SetStatus($"{game} · {model.Message}");
    }

    private void ToggleRunning()
    {
        _running = !_running;
        _runVersion++;
        _loadMonitor.SetEnabled(_running);
        _idleLessons.Reset(Environment.TickCount64);
        if (!_running)
        {
            _translationCts?.Cancel();
            _retryRequest = null;
            _readyReply = null;
            _pendingDialogue = _pendingQuest = string.Empty;
            _speechService.Stop();
            _speechActive = false;
            _recentSentences.Clear();
            _recentQuests.Clear();
        }
        _startButton.Text = _running ? "關閉" : "開啟";
        _startButton.ForeColor = _running ? Color.FromArgb(248, 113, 113) : Color.FromArgb(167, 243, 208);
        _scanTimer.Enabled = _running && !_previewMode;
        SetStatus(_running ? "自動負載模式已啟用；戰鬥按鍵時只記錄，空白鍵不會暫緩教學。" : "已暫停");
        if (_running && !_previewMode)
            _ = ScanOnceAsync(force: false);
        BeginInvoke(RestoreGameFocus);
    }

    private async void ScanTimerTick(object? sender, EventArgs eventArgs) => await ScanOnceAsync(force: false);

    private async Task ScanOnceAsync(bool force)
    {
        if (_captureBusy || (!force && (!_running || _settingsOpen)))
            return;
        var runVersion = _runVersion;
        _captureBusy = true;

        try
        {
            var load = _loadMonitor.Sample(_coachBusy);
            _scanTimer.Interval = load.ShouldDefer || _coachBusy
                ? Math.Clamp(_config.BusyScanIntervalMs, 1500, 8000)
                : Math.Clamp(_config.ScanIntervalMs, 700, 5000);
            if (_gameWindow is null || !CaptureService.TryRefresh(_gameWindow, out var refreshed))
            {
                _gameWindow = CaptureService.FindDiabloWindow();
                if (_gameWindow is null)
                {
                    _translationCts?.Cancel();
                    _speechService.Stop();
                    _speechActive = false;
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
                _idleLessons.TryNext(Environment.TickCount64, false, _config);
                if (_speechActive)
                {
                    _speechService.Stop();
                    _speechActive = false;
                }
                // CPU load caused by our own inference must not cancel itself.
                // New action keys or leaving the game do cancel that request.
                if (_coachBusy && (!gameForeground || load.ActionKeyIdleMs < AdaptiveLoadMonitor.ActiveKeyThresholdMs))
                    _translationCts?.Cancel();
                if (!gameForeground)
                    return;
            }
            if (_coachBusy && !force)
                return;

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

            if (force)
            {
                // Settings diagnostics must not turn the coach on or call LLM/TTS.
                SetStatus($"辨識測試：對話{(OcrService.LooksLikeEnglishSubtitle(dialogueText) ? "✓" : "－")} · 任務{(OcrService.LooksLikeEnglishSubtitle(questText) ? "✓" : "－")}");
                _originalLabel.Text = $"QUEST · {questText}";
                _simpleLabel.Text = dialogueText;
                return;
            }

            // Do not start inference if the player pressed a key during OCR.
            load = _loadMonitor.Sample(_coachBusy);
            if (NativeMethods.GetForegroundWindow() != _gameWindow.Handle)
                return;
            if (OcrService.LooksLikeEnglishSubtitle(dialogueText))
                _lastDialogueAt = Environment.TickCount64;

            var deferCoaching = load.ShouldDefer;
            HandleRecognizedText(dialogueText, CaptureRegionKind.Dialogue, deferCoaching, load.Reason);
            HandleRecognizedText(questText, CaptureRegionKind.Quest, deferCoaching, load.Reason);

            if (!deferCoaching && !_coachBusy)
            {
                if (_readyReply is not null)
                {
                    var reply = _readyReply;
                    _readyReply = null;
                    DisplayReply(reply);
                }
                else
                    ProcessDeferredLesson();
                TryIdleLesson(load);
            }
            else
                _idleLessons.TryNext(Environment.TickCount64, false, _config);
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

            if (_candidateCount >= 2 && !_recentSentences.Any(previous => Similar(previous, text) >= 0.90))
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

            if (_questCandidateCount >= 2 && !_recentQuests.Any(previous => Similar(previous, text) >= 0.90))
                AcceptQuest(text, deferCoaching, loadReason);
        }

        return true;
    }

    private void AcceptSubtitle(string text, bool deferCoaching = false, string loadReason = "")
    {
        _currentDialogue = text;
        _readyReply = null;
        _retryRequest = null;
        _recentSentences.Enqueue(text);
        while (_recentSentences.Count > 12)
            _recentSentences.Dequeue();

        _pendingDialogue = text;
        if (deferCoaching)
            SetStatus($"{loadReason} · 已記住新對話，空閒時補充英文。");
    }

    private void AcceptQuest(string text, bool deferCoaching = false, string loadReason = "")
    {
        _readyReply = null;
        _retryRequest = null;
        _recentQuests.Enqueue(text);
        while (_recentQuests.Count > 12)
            _recentQuests.Dequeue();

        _pendingQuest = text;
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
            StartCoachRequest(
                $"QUEST · {quest}",
                "電腦已空閒；正在補充任務指引與英文……",
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
                "電腦已空閒；正在補充剛才的英文……",
                token => _coachService.ExplainAsync(dialogue, _config, token));
            return;
        }
        var retry = _retryRequest;
        _retryRequest = null;
        retry?.Invoke();
    }

    private void TryIdleLesson(LoadSnapshot load)
    {
        var now = Environment.TickCount64;
        var safe = _running && !_coachBusy && !_settingsOpen && !load.ShouldDefer &&
                   load.CpuPercent < 55 && load.ActionKeyIdleMs >= IdleLessonPlanner.QuietWindowMs &&
                   now - _lastDialogueAt >= 12_000 &&
                   _pendingQuest.Length == 0 && _pendingDialogue.Length == 0 && _readyReply is null;
        var lesson = _idleLessons.TryNext(now, safe, _config);
        if (lesson is null)
            return;
        // Keep the quest and its translation visible. Supplement only the right column.
        _keywordsLabel.Text = $"{lesson.Title}\n{lesson.English}\n{lesson.Chinese}";
        SetStatus("空閒補充 · 本機教材 · 無額外模型呼叫");
        SpeakLesson(lesson.English, lesson.Chinese);
    }

    private void SpeakLesson(string english, string chinese)
    {
        if (_config.SpeakChinese)
            _speechService.SpeakTraditionalChinese(chinese);
        else if (_config.SpeakEnglish)
            _speechService.SpeakEnglish(english);
        _speechActive = _config.SpeakChinese || _config.SpeakEnglish;
    }

    private void DisplayReply(CoachReply reply)
    {
        _originalLabel.Text = reply.Original;
        _simpleLabel.Text = reply.SimpleEnglish;
        _chineseLabel.Text = reply.TraditionalChinese;
        var words = reply.Keywords.Count == 0 ? "這句先不用背新單字" :
            string.Join("    ", reply.Keywords.Select(keyword => $"{keyword.Word}＝{keyword.Meaning}"));
        _keywordsLabel.Text = reply.Advice is null ? words : $"一般建議 · 非背包分析\n{reply.Advice}";
        SetStatus(reply.Notice ?? (reply.UsedLocalModel ? "任務與英文教學完成" : "本機基本教學"));
        _idleLessons.ObserveLesson(Environment.TickCount64, reply.Keywords);
        SpeakLesson(reply.SimpleEnglish, reply.TraditionalChinese);
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
        if (!_running || _settingsOpen || IsDisposed)
            return;
        _retryRequest = () => StartCoachRequest(original, status, request);
        _idleLessons.Reset(Environment.TickCount64);
        _speechService.Stop();
        _speechActive = false;
        _originalLabel.Text = original;
        _simpleLabel.Text = "教練正在思考……";
        _chineseLabel.Text = "正在整理不爆雷的說明……";
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
            var load = _loadMonitor.Sample(false);
            if (_settingsOpen || load.ShouldDefer || _gameWindow is null ||
                NativeMethods.GetForegroundWindow() != _gameWindow.Handle)
                _readyReply = reply;
            else
                DisplayReply(reply);
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
        _translationCts?.Cancel();
        _speechService.Stop();
        _speechActive = false;
        // Retain the menu until the next opening/form shutdown. Disposing in
        // Closed breaks ToolStrip's own click/close event sequence.
        menu.Closed += (_, _) =>
        {
            _settingsOpen = false;
            _idleLessons.Reset(Environment.TickCount64);
        };
        var recognition = new ToolStripMenuItem("辨識範圍");
        recognition.DropDownItems.Add("設定對話字幕區", null, (_, _) => PickRegion(CaptureRegionKind.Dialogue));
        recognition.DropDownItems.Add("設定任務目標區", null, (_, _) => PickRegion(CaptureRegionKind.Quest));
        recognition.DropDownItems.Add("測試兩個辨識區", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(recognition);

        menu.Items.Add("聲音與語速", null, (_, _) => OpenVoiceSettings());

        var opacity = new ToolStripMenuItem("透明度");
        foreach (var value in new[] { 0.65, 0.80, 0.92 })
        {
            var item = new ToolStripMenuItem($"{value:P0}") { Checked = Math.Abs(Opacity - value) < 0.02 };
            item.Click += (_, _) => SetOverlayOpacity(value);
            opacity.DropDownItems.Add(item);
        }
        menu.Items.Add(opacity);

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

        menu.Show(_settingsButton, new Point(0, _settingsButton.Height));
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

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _running = false;
        _runVersion++;
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        _config.Save();
        _scanTimer.Stop();
        _lifetimeCts.Cancel();
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
        if (!buttons.SequenceEqual(new[] { "開啟", "設定", "×" }))
            return false;
        for (var i = 0; i < 8; i++)
        {
            ShowSettingsMenu();
            Application.DoEvents();
            _settingsMenu!.Close();
            Application.DoEvents();
            if (_settingsOpen || _settingsMenu.IsDisposed)
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
        _originalLabel.Text = "QUEST · Head to Guard's Watch";
        _simpleLabel.Text = "Go to Guard's Watch.";
        _chineseLabel.Text = "現在要做：前往 Guard's Watch。\n英文小知識：head to＝前往某地。";
        _keywordsLabel.Text = "一般配裝建議 · 非背包分析\nBuild = 技能與裝備的搭配\n先選一個常用核心技能，再挑強化該技能的效果。";
        SetStatus("預覽 · 任務優先，空閒時補充英文／流派");
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
