namespace DiabloEnglishCoach;

internal sealed class TranslationSettingsForm : Form
{
    private readonly CoachConfig _config;
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly TextBox _region = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true };
    private readonly RadioButton _local = new() { Text = "本機快速翻譯（推薦，不需帳號）", AutoSize = true };
    private readonly RadioButton _azure = new() { Text = "Azure Translator（選用）", AutoSize = true };

    public TranslationSettingsForm(CoachConfig config)
    {
        _config = config;
        Text = "翻譯方式";
        Size = new Size(640, 410);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        BackColor = Color.FromArgb(22, 24, 31);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 10);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        var explanation = new Label
        {
            AutoSize = true, MaximumSize = new Size(585, 0),
            Text = "推薦使用 qwen3.5:0.8b 本機小模型：免帳號、文字不上傳。第一次只要執行「安裝本機翻譯.cmd」下載約 1 GB。Azure 保留為選用備援。"
        };
        root.Controls.Add(explanation, 0, 0); root.SetColumnSpan(explanation, 2);
        _local.Checked = _config.TranslationProvider != TranslationProviders.Azure;
        _azure.Checked = !_local.Checked;
        var providers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        providers.Controls.AddRange([_local, _azure]);
        root.Controls.Add(providers, 0, 1); root.SetColumnSpan(providers, 2);
        _local.CheckedChanged += (_, _) => UpdateAzureControls();
        root.Controls.Add(new Label { Text = "Azure Key", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _key.PlaceholderText = AzureCredentialStore.Exists() ? "已儲存；留白表示不更換" : "貼上 Azure Translator 金鑰";
        root.Controls.Add(_key, 1, 2);
        root.Controls.Add(new Label { Text = "Azure 區域", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _region.Text = _config.AzureTranslatorRegion;
        _region.PlaceholderText = "依 Azure 資源頁填寫，例如 eastasia；若未要求可留白";
        root.Controls.Add(_region, 1, 3);
        _status.Text = _local.Checked ? $"狀態：本機翻譯 · {_config.TranslationModel}" :
            AzureCredentialStore.Exists() ? "狀態：Windows 已保存 Azure 金鑰" : "狀態：尚未保存 Azure 金鑰";
        _status.ForeColor = Color.FromArgb(167, 243, 208);
        root.Controls.Add(_status, 0, 4); root.SetColumnSpan(_status, 2);
        var note = new Label { AutoSize = true, MaximumSize = new Size(585, 0), ForeColor = Color.FromArgb(156, 163, 175),
            Text = "本機模式只連到 127.0.0.1 的 Ollama。Azure 模式才會把過濾後的英文送給 Microsoft；金鑰只存 Windows 認證管理員。" };
        root.Controls.Add(note, 0, 5); root.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = MakeButton("儲存並關閉", (_, _) => SaveAndClose());
        var remove = MakeButton("移除本機金鑰", (_, _) => RemoveKey());
        var test = MakeButton("測試翻譯");
        test.Click += async (_, _) => await TestAsync(test);
        buttons.Controls.AddRange([save, test, remove]);
        root.Controls.Add(buttons, 0, 6); root.SetColumnSpan(buttons, 2);
        UpdateAzureControls();
    }

    private void SaveAndClose()
    {
        try
        {
            if (_key.TextLength > 0) AzureCredentialStore.Write(_key.Text);
            _config.AzureTranslatorRegion = _region.Text.Trim();
            _config.OnlineTranslationEnabled = AzureCredentialStore.Exists();
            _config.TranslationProvider = _azure.Checked ? TranslationProviders.Azure : TranslationProviders.LocalOllama;
            _config.Save();
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法保存金鑰", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void RemoveKey()
    {
        try
        {
            AzureCredentialStore.Delete();
            _config.OnlineTranslationEnabled = false;
            _config.Save();
            _key.Clear(); _status.Text = "狀態：金鑰已移除";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法移除金鑰", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task TestAsync(Button button)
    {
        var key = _key.TextLength > 0 ? _key.Text : AzureCredentialStore.Read();
        if (_azure.Checked && string.IsNullOrWhiteSpace(key))
        {
            _status.Text = "狀態：請先貼上金鑰";
            _status.ForeColor = Color.FromArgb(253, 186, 116);
            return;
        }
        button.Enabled = false;
        _status.Text = _azure.Checked ? "狀態：正在測試官方 API……" : "狀態：正在測試本機小模型……";
        var testPath = Path.Combine(Path.GetTempPath(), $"diablo-translation-test-{Guid.NewGuid():N}.json");
        try
        {
            using var service = new FastTranslationService(cachePath: testPath, readKey: () => key);
            var config = new CoachConfig
            {
                OnlineTranslationEnabled = true,
                AzureTranslatorRegion = _region.Text.Trim(),
                TranslationProvider = _azure.Checked ? TranslationProviders.Azure : TranslationProviders.LocalOllama,
                TranslationModel = _config.TranslationModel,
                OllamaUrl = _config.OllamaUrl,
                InferenceThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
                ForceCpuInference = true
            };
            var result = await service.TranslateAsync("This is a translation test.", false, config, default);
            _status.Text = result.Text is null
                ? $"狀態：測試失敗 · {result.Source}"
                : $"狀態：成功 · {result.Text} · {result.ElapsedMs} ms";
            _status.ForeColor = result.Text is null ? Color.FromArgb(253, 186, 116) : Color.FromArgb(167, 243, 208);
        }
        catch (Exception ex)
        {
            _status.Text = $"狀態：測試失敗 · {ex.Message}";
            _status.ForeColor = Color.FromArgb(253, 186, 116);
        }
        finally
        {
            button.Enabled = true;
            try { File.Delete(testPath); File.Delete(testPath + ".tmp"); } catch { }
        }
    }
    private void UpdateAzureControls()
    {
        _key.Enabled = _region.Enabled = _azure.Checked;
        if (_local.Checked)
            _status.Text = $"狀態：本機翻譯 · {_config.TranslationModel}";
    }
    private static Button MakeButton(string text, EventHandler? click = null)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 42, 52), ForeColor = Color.WhiteSmoke, Padding = new Padding(6, 3, 6, 3) };
        if (click is not null) button.Click += click;
        return button;
    }
}
