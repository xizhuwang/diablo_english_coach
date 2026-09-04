namespace DiabloEnglishCoach;

internal sealed class VoiceSettingsForm : Form
{
    private sealed record VoiceChoice(string Label, string Id)
    {
        public override string ToString() => Label;
    }

    private readonly CoachConfig _config;
    private readonly SpeechService _speech;
    private readonly CheckBox _online = new();
    private readonly ComboBox _chineseVoice = new();
    private readonly ComboBox _englishVoice = new();
    private readonly TrackBar _rate = new();
    private readonly TrackBar _pitch = new();
    private readonly Label _rateValue = new();
    private readonly Label _pitchValue = new();
    private readonly CheckBox _autoChinese = new();
    private readonly CheckBox _autoEnglish = new();

    public VoiceSettingsForm(CoachConfig config, SpeechService speech)
    {
        _config = config;
        _speech = speech;
        Text = "聲音設定";
        Size = new Size(510, 485);
        MinimumSize = new Size(480, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = Color.FromArgb(22, 24, 31);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 10);

        BuildUi();
        LoadConfig();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 8,
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 8; index++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _online.Text = "使用自然女聲（需要網路；失敗時自動改用離線語音）";
        _online.AutoSize = true;
        _online.ForeColor = Color.FromArgb(167, 243, 208);
        root.Controls.Add(_online, 0, 0);
        root.SetColumnSpan(_online, 2);

        _chineseVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _chineseVoice.Dock = DockStyle.Fill;
        _chineseVoice.Items.AddRange(new object[]
        {
            new VoiceChoice("小雨－較年輕、活潑（推薦）", "zh-TW-HsiaoYuNeural"),
            new VoiceChoice("曉臻－溫和、自然", "zh-TW-HsiaoChenNeural"),
            new VoiceChoice("雲哲－沉穩男聲", "zh-TW-YunJheNeural")
        });
        AddRow(root, 1, "繁中聲音", _chineseVoice);

        _englishVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _englishVoice.Dock = DockStyle.Fill;
        _englishVoice.Items.AddRange(new object[]
        {
            new VoiceChoice("Ana－年輕可愛（推薦）", "en-US-AnaNeural"),
            new VoiceChoice("Jenny－親切清楚", "en-US-JennyNeural"),
            new VoiceChoice("Aria－自然成熟", "en-US-AriaNeural")
        });
        AddRow(root, 2, "英文聲音", _englishVoice);

        _rate.Minimum = -30;
        _rate.Maximum = 50;
        _rate.TickFrequency = 10;
        _rate.Dock = DockStyle.Fill;
        _rate.Scroll += (_, _) => _rateValue.Text = FormatSigned(_rate.Value, "%");
        var ratePanel = MakeSliderPanel(_rate, _rateValue);
        AddRow(root, 3, "語速", ratePanel);

        _pitch.Minimum = -10;
        _pitch.Maximum = 20;
        _pitch.TickFrequency = 5;
        _pitch.Dock = DockStyle.Fill;
        _pitch.Scroll += (_, _) => _pitchValue.Text = FormatSigned(_pitch.Value, " Hz");
        var pitchPanel = MakeSliderPanel(_pitch, _pitchValue);
        AddRow(root, 4, "音調", pitchPanel);

        var autoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _autoChinese.Text = "翻譯完成自動念中文";
        _autoChinese.AutoSize = true;
        _autoEnglish.Text = "改成自動念英文";
        _autoEnglish.AutoSize = true;
        _autoChinese.CheckedChanged += (_, _) => { if (_autoChinese.Checked) _autoEnglish.Checked = false; };
        _autoEnglish.CheckedChanged += (_, _) => { if (_autoEnglish.Checked) _autoChinese.Checked = false; };
        autoPanel.Controls.AddRange(new Control[] { _autoChinese, _autoEnglish });
        root.Controls.Add(autoPanel, 0, 5);
        root.SetColumnSpan(autoPanel, 2);

        var previewPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var previewChinese = MakeButton("試聽中文", (_, _) =>
        {
            Apply();
            _speech.SpeakTraditionalChinese("嗨！我是你今天的英文冒險夥伴，我們一起出發吧！");
        });
        var previewEnglish = MakeButton("試聽英文", (_, _) =>
        {
            Apply();
            _speech.SpeakEnglish("Hey! Ready for our next adventure?");
        });
        previewPanel.Controls.AddRange(new Control[] { previewChinese, previewEnglish });
        root.Controls.Add(previewPanel, 0, 6);
        root.SetColumnSpan(previewPanel, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(MakeButton("儲存並關閉", (_, _) => { Apply(); Close(); }));
        root.Controls.Add(footer, 0, 7);
        root.SetColumnSpan(footer, 2);
    }

    private void LoadConfig()
    {
        _online.Checked = _config.UseOnlineNeuralVoice;
        SelectVoice(_chineseVoice, _config.ChineseVoice);
        SelectVoice(_englishVoice, _config.EnglishVoice);
        _rate.Value = Math.Clamp(_config.SpeechRatePercent, _rate.Minimum, _rate.Maximum);
        _pitch.Value = Math.Clamp(_config.SpeechPitchHz, _pitch.Minimum, _pitch.Maximum);
        _rateValue.Text = FormatSigned(_rate.Value, "%");
        _pitchValue.Text = FormatSigned(_pitch.Value, " Hz");
        _autoChinese.Checked = _config.SpeakChinese;
        _autoEnglish.Checked = _config.SpeakEnglish;
    }

    private void Apply()
    {
        _config.UseOnlineNeuralVoice = _online.Checked;
        _config.ChineseVoice = (_chineseVoice.SelectedItem as VoiceChoice)?.Id ?? "zh-TW-HsiaoYuNeural";
        _config.EnglishVoice = (_englishVoice.SelectedItem as VoiceChoice)?.Id ?? "en-US-AnaNeural";
        _config.SpeechRatePercent = _rate.Value;
        _config.SpeechPitchHz = _pitch.Value;
        _config.SpeakChinese = _autoChinese.Checked;
        _config.SpeakEnglish = _autoEnglish.Checked;
        _config.Save();
    }

    private static void SelectVoice(ComboBox comboBox, string id)
    {
        comboBox.SelectedIndex = 0;
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is VoiceChoice choice && choice.Id == id)
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private static void AddRow(TableLayoutPanel panel, int row, string title, Control control)
    {
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(209, 213, 219) }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static Control MakeSliderPanel(TrackBar slider, Label value)
    {
        value.AutoSize = true;
        value.Width = 60;
        value.TextAlign = ContentAlignment.MiddleRight;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        panel.Controls.Add(slider, 0, 0);
        panel.Controls.Add(value, 1, 0);
        return panel;
    }

    private static Button MakeButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 42, 52),
            ForeColor = Color.WhiteSmoke,
            Padding = new Padding(7, 3, 7, 3)
        };
        button.Click += handler;
        return button;
    }

    private static string FormatSigned(int value, string suffix) => value >= 0 ? $"+{value}{suffix}" : $"{value}{suffix}";
}
