using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace DiabloEnglishCoach;

internal static class CaptureService
{
    public static IReadOnlyList<WindowInfo> ListCandidateWindows()
    {
        var results = new List<WindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
                return true;

            var length = NativeMethods.GetWindowTextLength(handle);
            if (length <= 0)
                return true;

            var titleBuffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString().Trim();
            if (title.Length == 0 || !TryGetClientBounds(handle, out var bounds) || bounds.Width < 640 || bounds.Height < 360)
                return true;

            results.Add(new WindowInfo(handle, title, bounds));
            return true;
        }, nint.Zero);

        return results
            .OrderByDescending(window => IsDiabloWindow(window.Title))
            .ThenByDescending(window => window.ClientBounds.Width * window.ClientBounds.Height)
            .ToArray();
    }

    public static WindowInfo? FindDiabloWindow() =>
        ListCandidateWindows().FirstOrDefault(window => IsDiabloWindow(window.Title));

    public static bool TryRefresh(WindowInfo window, out WindowInfo refreshed)
    {
        if (NativeMethods.IsWindowVisible(window.Handle) &&
            !NativeMethods.IsIconic(window.Handle) &&
            TryGetClientBounds(window.Handle, out var bounds))
        {
            refreshed = window with { ClientBounds = bounds };
            return true;
        }

        refreshed = window;
        return false;
    }

    public static Bitmap CaptureClient(WindowInfo window)
    {
        if (!TryRefresh(window, out var refreshed))
            throw new InvalidOperationException("遊戲視窗目前不可見，或已最小化。");

        return CaptureScreen(refreshed.ClientBounds);
    }

    public static Bitmap CaptureRegion(WindowInfo window, CoachConfig config, CaptureRegionKind kind = CaptureRegionKind.Dialogue)
    {
        if (!TryRefresh(window, out var refreshed))
            throw new InvalidOperationException("遊戲視窗目前不可見，或已最小化。");

        var client = refreshed.ClientBounds;
        var x = kind == CaptureRegionKind.Dialogue ? config.RegionX : config.QuestRegionX;
        var y = kind == CaptureRegionKind.Dialogue ? config.RegionY : config.QuestRegionY;
        var width = kind == CaptureRegionKind.Dialogue ? config.RegionWidth : config.QuestRegionWidth;
        var height = kind == CaptureRegionKind.Dialogue ? config.RegionHeight : config.QuestRegionHeight;
        var rectangle = new Rectangle(
            client.Left + (int)Math.Round(client.Width * x),
            client.Top + (int)Math.Round(client.Height * y),
            Math.Max(1, (int)Math.Round(client.Width * width)),
            Math.Max(1, (int)Math.Round(client.Height * height)));

        return CaptureScreen(rectangle);
    }

    public static Bitmap PrepareForOcr(Bitmap source)
    {
        const int maximumWidth = 2400;
        var scale = Math.Min(2.0, maximumWidth / (double)Math.Max(1, source.Width));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        // Subtitles are usually bright over a dark outline. Raising contrast makes
        // the built-in Windows OCR much more reliable without another dependency.
        using var adjusted = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(adjusted))
        using (var attributes = new ImageAttributes())
        {
            var matrix = new ColorMatrix(new[]
            {
                new[] { 1.45f, 0f, 0f, 0f, 0f },
                new[] { 0f, 1.45f, 0f, 0f, 0f },
                new[] { 0f, 0f, 1.45f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { -0.20f, -0.20f, -0.20f, 0f, 1f }
            });
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(resized, new Rectangle(0, 0, width, height), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
        }

        resized.Dispose();
        return (Bitmap)adjusted.Clone();
    }

    private static Bitmap CaptureScreen(Rectangle rectangle)
    {
        var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rectangle.Location, Point.Empty, rectangle.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static bool TryGetClientBounds(nint handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!NativeMethods.GetClientRect(handle, out var rectangle))
            return false;

        var topLeft = new NativeMethods.NativePoint { X = rectangle.Left, Y = rectangle.Top };
        var bottomRight = new NativeMethods.NativePoint { X = rectangle.Right, Y = rectangle.Bottom };
        if (!NativeMethods.ClientToScreen(handle, ref topLeft) || !NativeMethods.ClientToScreen(handle, ref bottomRight))
            return false;

        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool IsDiabloWindow(string title) =>
        title.Contains("Diablo Immortal", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("暗黑破壞神 永生不朽", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("暗黑破壞神：永生不朽", StringComparison.OrdinalIgnoreCase);
}
