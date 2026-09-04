using System.Text;

namespace DiabloEnglishCoach;

internal sealed class CoachForm : Form
{
    private readonly CoachConfig _config = CoachConfig.Load();
    private readonly OcrService _ocrService;
    private readonly CoachService _coachService = new();
    private readonly SpeechService _speechService;
    private readonly System.Windows.Forms.Timer _scanTimer = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _translationCts;
    private WindowInfo? _gameWindow;
    private bool _captureBusy;
    private bool _running;
    private string _candidate = string.Empty;
    private int _candidateCount;
    private readonly Queue<string> _recentSentences = new();

    private readonly Label _statusLabel = new();
    private readonly Label _originalLabel = new();
    private readonly Label _simpleLabel = new();
    private readonly Label _chineseLabel = new();
    private readonly Label _keywordsLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _regionButton = new();
    private readonly Button _testButton = new();
    private readonly Button _speakEnglishButton = new();
    private readonly Button _speakChineseButton = new();
    private readonly Button _voiceButton = new();
    private readonly Button _opacityButton = new();
    private readonly Button _collapseButton = new();
    private readonly Panel _contentPanel = new();
    private const int ExpandedHeight = 158;
    private const int CompactHeight = 38;

    public CoachForm(bool previewMode = false)
    {
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
        Size = new Size(overlayWidth, _config.CompactMode ? CompactHeight : ExpandedHeight);
        MinimumSize = new Size(700, CompactHeight);
        MaximumSize = new Size(1600, ExpandedHeight);
        var defaultLocation = new Point(area.Left + (area.Width - Width) / 2, area.Top + 10);
        Location = SavedLocationIsVisible(area)
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
        ConfigureCompactButton(_startButton, "▶", "開始／暫停自動教學", (_, _) => ToggleRunning(), tips);
        ConfigureCompactButton(_regionButton, "範圍", "重新框選字幕區", (_, _) => PickRegion(), tips);
        ConfigureCompactButton(_testButton, "測試", "測試目前字幕辨識", async (_, _) => await TestCaptureAsync(), tips);
        ConfigureCompactButton(_speakEnglishButton, "EN", "朗讀英文原句", (_, _) => _speechService.SpeakEnglish(_originalLabel.Text), tips);
        ConfigureCompactButton(_speakChineseButton, "中", "朗讀繁中翻譯", (_, _) => _speechService.SpeakTraditionalChinese(_chineseLabel.Text), tips);
        ConfigureCompactButton(_voiceButton, "聲音", "選擇聲線與語速", (_, _) => OpenVoiceSettings(), tips);
        ConfigureCompactButton(_opacityButton, $"{Opacity:P0}", "切換透明度：65%／80%／92%", (_, _) => CycleOpacity(), tips);
        ConfigureCompactButton(_collapseButton, _config.CompactMode ? "▾" : "▴", "收合／展開教練", (_, _) => ToggleCompactMode(), tips);
        var closeButton = new Button();
        ConfigureCompactButton(closeButton, "×", "關閉教練", (_, _) => Close(), tips);
        closeButton.ForeColor = Color.FromArgb(248, 113, 113);
        toolbar.Controls.AddRange(new Control[]
        {
            _startButton, _regionButton, _testButton, _speakEnglishButton,
            _speakChineseButton, _voiceButton, _opacityButton, _collapseButton, closeButton
        });
        topBar.Controls.Add(toolbar, 2, 0);

        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Margin = new Padding(0, 2, 0, 0);
        _contentPanel.Visible = !_config.CompactMode;
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
        contentGrid.Controls.Add(WrapSection("WORDS", _keywordsLabel), 2, 0);

        ApplyRoundedCorners();
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        _gameWindow = CaptureService.FindDiabloWindow();
        if (_gameWindow is not null && _config.WindowLeft < 0)
            PositionOverGameWindow(_gameWindow.ClientBounds);
        var model = await _coachService.CheckAsync(_config, _lifetimeCts.Token);
        var game = _gameWindow is null ? "尚未找到 Diablo Immortal 視窗" : "已找到遊戲視窗";
        SetStatus($"{game} · {model.Message}");
    }

    private void ToggleRunning()
    {
        _running = !_running;
        _startButton.Text = _running ? "Ⅱ" : "▶";
        _scanTimer.Enabled = _running;
        SetStatus(_running ? "正在等待新的英文字幕……" : "已暫停");
        if (_running)
            _ = ScanOnceAsync(force: false);
    }

    private async void ScanTimerTick(object? sender, EventArgs eventArgs) => await ScanOnceAsync(force: false);

    private async Task ScanOnceAsync(bool force)
    {
        if (_captureBusy)
            return;
        _captureBusy = true;

        try
        {
            if (_gameWindow is null || !CaptureService.TryRefresh(_gameWindow, out var refreshed))
            {
                _gameWindow = CaptureService.FindDiabloWindow();
                if (_gameWindow is null)
                {
                    SetStatus("找不到 Diablo Immortal。請先開啟遊戲並使用視窗化全螢幕。");
                    return;
                }
            }
            else
            {
                _gameWindow = refreshed;
            }

            using var capture = CaptureService.CaptureRegion(_gameWindow, _config);
            using var prepared = CaptureService.PrepareForOcr(capture);
            var text = await _ocrService.RecognizeAsync(prepared, _lifetimeCts.Token);

            if (!OcrService.LooksLikeEnglishSubtitle(text))
            {
                if (force)
                    SetStatus(text.Length == 0 ? "這個範圍沒有辨識到文字，請重新框選字幕區。" : $"有讀到文字，但不像英文字幕：{text}");
                return;
            }

            if (force)
            {
                AcceptSubtitle(text);
                return;
            }

            if (Similar(_candidate, text) >= 0.84)
                _candidateCount++;
            else
            {
                _candidate = text;
                _candidateCount = 1;
            }

            if (_candidateCount >= 2 && !_recentSentences.Any(previous => Similar(previous, text) >= 0.90))
                AcceptSubtitle(text);
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

    private void AcceptSubtitle(string text)
    {
        _originalLabel.Text = text;
        _simpleLabel.Text = "正在用簡單英文改寫……";
        _chineseLabel.Text = "正在產生不爆雷的繁中解釋……";
        _keywordsLabel.Text = "正在找重點單字……";
        SetStatus("字幕已讀取；本機教練正在思考……");

        // English can be spoken immediately because it does not depend on the
        // local translation model. This removes the biggest perceived delay.
        if (_config.SpeakEnglish)
            _speechService.SpeakEnglish(text);

        _recentSentences.Enqueue(text);
        while (_recentSentences.Count > 12)
            _recentSentences.Dequeue();

        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _ = ExplainAndDisplayAsync(text, _translationCts.Token);
    }

    private async Task ExplainAndDisplayAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _coachService.ExplainAsync(text, _config, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            _simpleLabel.Text = reply.SimpleEnglish;
            _chineseLabel.Text = reply.TraditionalChinese;
            _keywordsLabel.Text = reply.Keywords.Count == 0
                ? "這句先不用背新單字"
                : string.Join("    ", reply.Keywords.Select(keyword => $"{keyword.Word}＝{keyword.Meaning}"));
            SetStatus(reply.Notice ?? (reply.UsedLocalModel ? $"完成 · {_config.Model} · 僅使用當前字幕" : "完成 · 基本離線模式"));

            if (_config.SpeakChinese)
                _speechService.SpeakTraditionalChinese(reply.TraditionalChinese);
        }
        catch (OperationCanceledException)
        {
            // A newer subtitle replaced this one.
        }
    }

    private void PickRegion()
    {
        _gameWindow ??= CaptureService.FindDiabloWindow();
        if (_gameWindow is null)
        {
            MessageBox.Show("請先開啟 Diablo Immortal，再按一次「框選字幕區」。", "找不到遊戲", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var wasRunning = _running;
        _scanTimer.Stop();
        Hide();
        try
        {
            Thread.Sleep(180);
            using var screenshot = CaptureService.CaptureClient(_gameWindow);
            using var picker = new RegionPickerForm(screenshot, _config);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                var selection = picker.SelectedImageRectangle;
                _config.RegionX = selection.X / (double)screenshot.Width;
                _config.RegionY = selection.Y / (double)screenshot.Height;
                _config.RegionWidth = selection.Width / (double)screenshot.Width;
                _config.RegionHeight = selection.Height / (double)screenshot.Height;
                _config.Save();
                _candidate = string.Empty;
                _candidateCount = 0;
                SetStatus("字幕區已儲存。建議在字幕出現時按「測試辨識」。");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "無法框選", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Show();
            Activate();
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
        using var settings = new VoiceSettingsForm(_config, _speechService);
        settings.ShowDialog(this);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        _config.Save();
        _scanTimer.Stop();
        _lifetimeCts.Cancel();
        _translationCts?.Cancel();
        _speechService.Dispose();
        _translationCts?.Dispose();
        _lifetimeCts.Dispose();
    }

    private void SetStatus(string text) => _statusLabel.Text = $"OCR：{_ocrService.RecognizerLanguage} · {text}";

    private void CycleOpacity()
    {
        Opacity = Opacity < 0.73 ? 0.80 : Opacity < 0.86 ? 0.92 : 0.65;
        _config.WindowOpacity = Opacity;
        _opacityButton.Text = $"{Opacity:P0}";
        _config.Save();
    }

    private void ToggleCompactMode()
    {
        _config.CompactMode = !_config.CompactMode;
        _contentPanel.Visible = !_config.CompactMode;
        Height = _config.CompactMode ? CompactHeight : ExpandedHeight;
        _collapseButton.Text = _config.CompactMode ? "▾" : "▴";
        _config.Save();
        ApplyRoundedCorners();
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
            gameBounds.Top + 10);
        ClampToVisibleScreen();
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
        button.Width = text is "範圍" or "測試" or "聲音"
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
