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

    public CoachForm()
    {
        _ocrService = new OcrService();
        _speechService = new SpeechService(_config);
        _speechService.StatusChanged += message => SetStatus(message);
        InitializeUi();
        _scanTimer.Interval = Math.Clamp(_config.ScanIntervalMs, 700, 5000);
        _scanTimer.Tick += ScanTimerTick;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WdaExcludeFromCapture);
    }

    private void InitializeUi()
    {
        Text = "Diablo English Coach";
        Size = new Size(570, 420);
        MinimumSize = new Size(500, 330);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(Math.Max(area.Left, area.Right - Width - 18), area.Top + 18);
        TopMost = true;
        BackColor = Color.FromArgb(22, 24, 31);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 10);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 27));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Text = "DIABLO · ENGLISH COACH",
            AutoSize = true,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 205, 76),
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(title, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        ConfigureButton(_startButton, "開始自動教學", (_, _) => ToggleRunning());
        ConfigureButton(_regionButton, "框選字幕區", (_, _) => PickRegion());
        ConfigureButton(_testButton, "測試辨識", async (_, _) => await TestCaptureAsync());
        ConfigureButton(_speakEnglishButton, "🔊 英文", (_, _) => _speechService.SpeakEnglish(_originalLabel.Text));
        ConfigureButton(_speakChineseButton, "🔊 中文", (_, _) => _speechService.SpeakTraditionalChinese(_chineseLabel.Text));
        ConfigureButton(_voiceButton, "聲音設定", (_, _) => OpenVoiceSettings());
        toolbar.Controls.AddRange(new Control[] { _startButton, _regionButton, _testButton, _speakEnglishButton, _speakChineseButton, _voiceButton });
        root.Controls.Add(toolbar, 0, 1);

        ConfigureContentLabel(_originalLabel, "等待英文字幕……", Color.WhiteSmoke, 11, FontStyle.Bold);
        ConfigureContentLabel(_simpleLabel, "簡單英文會顯示在這裡", Color.FromArgb(147, 197, 253), 10, FontStyle.Regular);
        ConfigureContentLabel(_chineseLabel, "繁體中文會顯示在這裡", Color.FromArgb(167, 243, 208), 11, FontStyle.Regular);
        ConfigureContentLabel(_keywordsLabel, "重點單字會顯示在這裡", Color.FromArgb(253, 230, 138), 10, FontStyle.Regular);
        root.Controls.Add(WrapSection("原句", _originalLabel), 0, 2);
        root.Controls.Add(WrapSection("簡單英文", _simpleLabel), 0, 3);
        root.Controls.Add(WrapSection("繁中（只解釋當前句）", _chineseLabel), 0, 4);
        root.Controls.Add(WrapSection("最多 3 個重點", _keywordsLabel), 0, 5);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(156, 163, 175);
        _statusLabel.Text = $"OCR：{_ocrService.RecognizerLanguage} · 未啟動";
        _statusLabel.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(_statusLabel, 0, 6);
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        _gameWindow = CaptureService.FindDiabloWindow();
        var model = await _coachService.CheckAsync(_config, _lifetimeCts.Token);
        var game = _gameWindow is null ? "尚未找到 Diablo Immortal 視窗" : "已找到遊戲視窗";
        SetStatus($"{game} · {model.Message}");
    }

    private void ToggleRunning()
    {
        _running = !_running;
        _startButton.Text = _running ? "暫停" : "開始自動教學";
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
        _scanTimer.Stop();
        _lifetimeCts.Cancel();
        _translationCts?.Cancel();
        _speechService.Dispose();
        _translationCts?.Dispose();
        _lifetimeCts.Dispose();
    }

    private void SetStatus(string text) => _statusLabel.Text = $"OCR：{_ocrService.RecognizerLanguage} · {text}";

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

    private static void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
        button.BackColor = Color.FromArgb(38, 42, 52);
        button.ForeColor = Color.WhiteSmoke;
        button.Padding = new Padding(5, 2, 5, 2);
        button.Margin = new Padding(0, 0, 7, 6);
        button.Click += onClick;
    }

    private static void ConfigureContentLabel(Label label, string text, Color color, float size, FontStyle style)
    {
        label.Text = text;
        label.ForeColor = color;
        label.Font = new Font("Microsoft JhengHei UI", size, style);
        label.Dock = DockStyle.Fill;
        label.AutoEllipsis = true;
        label.Padding = new Padding(0, 2, 0, 3);
    }

    private static Control WrapSection(string heading, Control content)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 3, 0, 3) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = heading,
            AutoSize = true,
            ForeColor = Color.FromArgb(156, 163, 175),
            Font = new Font("Microsoft JhengHei UI", 8, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }
}
