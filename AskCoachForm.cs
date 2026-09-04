namespace DiabloEnglishCoach;

internal sealed class AskCoachForm : Form
{
    private readonly TextBox _question = new();

    public string Question { get; private set; } = string.Empty;

    public AskCoachForm(bool hasQuest, bool hasDialogue)
    {
        Text = "問英文教練";
        Size = new Size(590, 205);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = Color.FromArgb(22, 24, 31);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 4,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = hasQuest || hasDialogue
                ? "教練會根據目前看得到的任務／對話回答，不會查後續攻略。"
                : "目前尚未讀到任務或對話；可以先問一般英文問題。",
            AutoSize = true,
            ForeColor = Color.FromArgb(167, 243, 208),
            Margin = new Padding(0, 0, 0, 8)
        });

        _question.Dock = DockStyle.Fill;
        _question.Multiline = true;
        _question.PlaceholderText = "例如：我現在要做什麼？head to 是什麼意思？考我剛才的單字……";
        _question.BackColor = Color.FromArgb(31, 35, 45);
        _question.ForeColor = Color.WhiteSmoke;
        _question.BorderStyle = BorderStyle.FixedSingle;
        _question.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && eventArgs.Control)
            {
                Submit(_question.Text);
                eventArgs.SuppressKeyPress = true;
            }
        };
        root.Controls.Add(_question);

        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        quick.Controls.Add(MakeButton("現在做什麼？", (_, _) => Submit("根據目前任務，我現在應該做什麼？順便教我任務裡最實用的英文片語。")));
        quick.Controls.Add(MakeButton("解釋剛才那句", (_, _) => Submit("請用簡單英文和繁中解釋剛才的對話，並告訴我一個實用句型。")));
        quick.Controls.Add(MakeButton("考我一題", (_, _) => Submit("請用目前畫面上的英文考我一題簡短的 A/B/C 選擇題。")));
        quick.Controls.Add(MakeButton("不爆雷提示", (_, _) => Submit("只根據目前看得到的任務，給我一個不爆雷的小提示。")));
        root.Controls.Add(quick);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(MakeButton("送出（Ctrl+Enter）", (_, _) => Submit(_question.Text)));
        footer.Controls.Add(MakeButton("取消", (_, _) => Close()));
        root.Controls.Add(footer);

        Shown += (_, _) => _question.Focus();
    }

    private void Submit(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return;
        Question = value;
        DialogResult = DialogResult.OK;
        Close();
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
            Padding = new Padding(5, 2, 5, 2),
            Margin = new Padding(0, 0, 7, 4)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
        button.Click += handler;
        return button;
    }
}
