namespace DiabloEnglishCoach;

internal sealed class TranslationSettingsForm : Form
{
    private readonly CoachConfig _config;
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly TextBox _region = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true };

    public TranslationSettingsForm(CoachConfig config)
    {
        _config = config;
        Text = "Azure Translator F0 設定";
        Size = new Size(590, 330);
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        var explanation = new Label
        {
            AutoSize = true, MaximumSize = new Size(535, 0),
            Text = "只支援 Azure Translator 官方 API。請在 Azure 建立 Translator 的 F0 資源，貼上 Key 1 或 Key 2。金鑰存入 Windows 認證管理員，不會寫入 config.json、程式碼或 GitHub。"
        };
        root.Controls.Add(explanation, 0, 0); root.SetColumnSpan(explanation, 2);
        root.Controls.Add(new Label { Text = "API 金鑰", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _key.PlaceholderText = AzureCredentialStore.Exists() ? "已儲存；留白表示不更換" : "貼上 Azure Translator 金鑰";
        root.Controls.Add(_key, 1, 1);
        root.Controls.Add(new Label { Text = "資源區域", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _region.Text = _config.AzureTranslatorRegion;
        _region.PlaceholderText = "依 Azure 資源頁填寫，例如 eastasia；若未要求可留白";
        root.Controls.Add(_region, 1, 2);
        _status.Text = AzureCredentialStore.Exists() ? "狀態：Windows 已保存金鑰" : "狀態：尚未保存金鑰";
        _status.ForeColor = Color.FromArgb(167, 243, 208);
        root.Controls.Add(_status, 0, 3); root.SetColumnSpan(_status, 2);
        var note = new Label { AutoSize = true, MaximumSize = new Size(535, 0), ForeColor = Color.FromArgb(156, 163, 175),
            Text = "遊戲英文會傳給 Microsoft 翻譯；不傳截圖、錄音或教練模型內容。F0 額度與 Azure 帳戶由你管理，本程式不會建立付費資源。" };
        root.Controls.Add(note, 0, 4); root.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = MakeButton("儲存並關閉", (_, _) => SaveAndClose());
        var remove = MakeButton("移除本機金鑰", (_, _) => RemoveKey());
        var test = MakeButton("測試翻譯");
        test.Click += async (_, _) => await TestAsync(test);
        buttons.Controls.AddRange([save, test, remove]);
        root.Controls.Add(buttons, 0, 5); root.SetColumnSpan(buttons, 2);
    }

    private void SaveAndClose()
    {
        try
        {
            if (_key.TextLength > 0) AzureCredentialStore.Write(_key.Text);
            _config.AzureTranslatorRegion = _region.Text.Trim();
            _config.OnlineTranslationEnabled = AzureCredentialStore.Exists();
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
        if (string.IsNullOrWhiteSpace(key))
        {
            _status.Text = "狀態：請先貼上金鑰";
            _status.ForeColor = Color.FromArgb(253, 186, 116);
            return;
        }
        button.Enabled = false;
        _status.Text = "狀態：正在測試官方 API……";
        var testPath = Path.Combine(Path.GetTempPath(), $"diablo-azure-test-{Guid.NewGuid():N}.json");
        try
        {
            using var service = new FastTranslationService(cachePath: testPath, readKey: () => key);
            var config = new CoachConfig
            {
                OnlineTranslationEnabled = true,
                AzureTranslatorRegion = _region.Text.Trim()
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
    private static Button MakeButton(string text, EventHandler? click = null)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 42, 52), ForeColor = Color.WhiteSmoke, Padding = new Padding(6, 3, 6, 3) };
        if (click is not null) button.Click += click;
        return button;
    }
}
