namespace DiabloEnglishCoach;

internal sealed class RegionPickerForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Rectangle _initialSelection;
    private readonly string _instruction;
    private Point _dragStart;
    private Rectangle _selection;
    private bool _dragging;

    public Rectangle SelectedImageRectangle { get; private set; }

    public RegionPickerForm(Bitmap screenshot, CoachConfig config, CaptureRegionKind kind, Rectangle gameClientBounds)
    {
        _screenshot = (Bitmap)screenshot.Clone();
        _instruction = kind == CaptureRegionKind.Dialogue
            ? "拖曳框選英文對話字幕區   Enter：儲存   Esc：取消\n只框字幕，不要包含任務清單、聊天窗與技能列"
            : "拖曳框選左側英文任務目標   Enter：儲存   Esc：取消\n只框任務名稱與目前目標，不要包含聊天窗";
        var x = kind == CaptureRegionKind.Dialogue ? config.RegionX : config.QuestRegionX;
        var y = kind == CaptureRegionKind.Dialogue ? config.RegionY : config.QuestRegionY;
        var width = kind == CaptureRegionKind.Dialogue ? config.RegionWidth : config.QuestRegionWidth;
        var height = kind == CaptureRegionKind.Dialogue ? config.RegionHeight : config.QuestRegionHeight;
        _initialSelection = new Rectangle(
            (int)Math.Round(screenshot.Width * x),
            (int)Math.Round(screenshot.Height * y),
            (int)Math.Round(screenshot.Width * width),
            (int)Math.Round(screenshot.Height * height));

        Text = kind == CaptureRegionKind.Dialogue ? "框選英文對話字幕區" : "框選英文任務目標區";
        StartPosition = FormStartPosition.Manual;
        Bounds = gameClientBounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        TopMost = true;

        Shown += (_, _) =>
        {
            _selection = ImageToClient(_initialSelection);
            Invalidate();
        };
        MouseDown += OnPickerMouseDown;
        MouseMove += OnPickerMouseMove;
        MouseUp += OnPickerMouseUp;
        KeyDown += OnPickerKeyDown;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var destination = ImageDestination();
        eventArgs.Graphics.DrawImage(_screenshot, destination);

        using var shade = new SolidBrush(Color.FromArgb(145, 0, 0, 0));
        eventArgs.Graphics.FillRectangle(shade, destination);

        if (!_selection.IsEmpty)
        {
            eventArgs.Graphics.SetClip(_selection);
            eventArgs.Graphics.DrawImage(_screenshot, destination);
            eventArgs.Graphics.ResetClip();
            using var pen = new Pen(Color.FromArgb(255, 210, 72), 3);
            eventArgs.Graphics.DrawRectangle(pen, _selection);
        }

        using var font = new Font("Microsoft JhengHei UI", 16, FontStyle.Bold);
        var size = eventArgs.Graphics.MeasureString(_instruction, font);
        var panel = new RectangleF(24, 24, size.Width + 28, size.Height + 22);
        using var panelBrush = new SolidBrush(Color.FromArgb(225, 20, 22, 28));
        eventArgs.Graphics.FillRectangle(panelBrush, panel);
        eventArgs.Graphics.DrawString(_instruction, font, Brushes.White, panel.X + 14, panel.Y + 10);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _screenshot.Dispose();
        base.Dispose(disposing);
    }

    private void OnPickerMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
            return;
        _dragStart = eventArgs.Location;
        _selection = Rectangle.Empty;
        _dragging = true;
    }

    private void OnPickerMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging)
            return;
        _selection = NormalizeRectangle(_dragStart, eventArgs.Location);
        Invalidate();
    }

    private void OnPickerMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging)
            return;
        _dragging = false;
        _selection = Rectangle.Intersect(_selection, ImageDestination());
        Invalidate();
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (eventArgs.KeyCode != Keys.Enter || _selection.Width < 40 || _selection.Height < 20)
            return;

        SelectedImageRectangle = ClientToImage(_selection);
        DialogResult = DialogResult.OK;
        Close();
    }

    private Rectangle ImageDestination()
    {
        var scale = Math.Min(ClientSize.Width / (double)_screenshot.Width, ClientSize.Height / (double)_screenshot.Height);
        var width = (int)Math.Round(_screenshot.Width * scale);
        var height = (int)Math.Round(_screenshot.Height * scale);
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    private Rectangle ImageToClient(Rectangle imageRectangle)
    {
        var destination = ImageDestination();
        var scale = destination.Width / (double)_screenshot.Width;
        return new Rectangle(
            destination.Left + (int)Math.Round(imageRectangle.Left * scale),
            destination.Top + (int)Math.Round(imageRectangle.Top * scale),
            (int)Math.Round(imageRectangle.Width * scale),
            (int)Math.Round(imageRectangle.Height * scale));
    }

    private Rectangle ClientToImage(Rectangle clientRectangle)
    {
        var destination = ImageDestination();
        var scale = _screenshot.Width / (double)destination.Width;
        return Rectangle.Intersect(new Rectangle(
            (int)Math.Round((clientRectangle.Left - destination.Left) * scale),
            (int)Math.Round((clientRectangle.Top - destination.Top) * scale),
            (int)Math.Round(clientRectangle.Width * scale),
            (int)Math.Round(clientRectangle.Height * scale)), new Rectangle(Point.Empty, _screenshot.Size));
    }

    private static Rectangle NormalizeRectangle(Point first, Point second) => Rectangle.FromLTRB(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
}
