using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DiabloEnglishCoach;

internal sealed partial class OcrService
{
    private OcrEngine _engine;
    private long _nextLanguageCheck;

    public string RecognizerLanguage { get; private set; }

    public OcrService()
    {
        var preferred = OcrEngine.AvailableRecognizerLanguages
            .FirstOrDefault(language => language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(language => language.LanguageTag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
            ?? OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();

        if (preferred is null)
            throw new InvalidOperationException("Windows 沒有可用的 OCR 語言。請在設定中加入英文或繁體中文的光學字元辨識功能。");

        _engine = OcrEngine.TryCreateFromLanguage(new Language(preferred.LanguageTag))
            ?? throw new InvalidOperationException($"無法啟動 Windows OCR：{preferred.LanguageTag}");
        RecognizerLanguage = preferred.LanguageTag;
    }

    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken, bool dialogue = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Windows may finish installing English OCR while the coach is open.
        // Probe at most once per minute; no download/admin action in the app.
        if (!RecognizerLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
            Environment.TickCount64 >= _nextLanguageCheck)
        {
            _nextLanguageCheck = Environment.TickCount64 + 60_000;
            var english = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(
                language => language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (english is not null && OcrEngine.TryCreateFromLanguage(english) is { } engine)
            {
                _engine = engine;
                RecognizerLanguage = english.LanguageTag;
            }
        }
        // Transfer BGRA pixels directly; PNG encoding/decoding on every scan
        // adds work and the Windows codec rejected some small fixture images.
        var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width * 4, bitmap.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        using var writer = new DataWriter();
        writer.WriteBytes(pixels);
        using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),
            BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
        // A single chat/ad line must not poison an otherwise useful subtitle.
        return DialogueText.Normalize(result.Lines.Select(line =>
        {
            var boxes = line.Words.Select(w => w.BoundingRect).ToArray();
            return boxes.Length == 0 ? new SubtitleLine(line.Text) : new SubtitleLine(line.Text,
                boxes.Min(b => b.Top), boxes.Max(b => b.Bottom) - boxes.Min(b => b.Top),
                boxes.Max(b => b.Right) - boxes.Min(b => b.Left));
        }), dialogue);
    }

    public async Task<string> RecognizeAdaptiveAsync(Bitmap bitmap, CancellationToken token, bool dialogue = false)
    {
        var raw = await RecognizeAsync(bitmap, token, dialogue);
        if (LooksLikeEnglishSubtitle(raw) && !DialogueText.NeedsRepair(raw)) return raw;
        using var prepared = CaptureService.PrepareForOcr(bitmap);
        var enhanced = await RecognizeAsync(prepared, token, dialogue);
        if (LooksLikeEnglishSubtitle(enhanced) && !DialogueText.NeedsRepair(enhanced) &&
            enhanced.Length >= raw.Length * .85) return enhanced;
        return raw.Length > 0 ? raw : enhanced;
    }

    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Replace('\r', ' ').Replace('\n', ' ');
        text = WhitespaceRegex().Replace(text, " ").Trim();
        text = DecorativeRegex().Replace(text, string.Empty).Trim();
        return text;
    }

    public static bool LooksLikeEnglishSubtitle(string text)
    {
        if (text.Length < 4 || text.Length > 700 || IsChatOrAdvertising(text))
            return false;

        var letters = text.Count(char.IsLetter);
        var latin = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        var words = WordRegex().Matches(text).Count;
        return words >= 2 && letters > 0 && latin / (double)letters >= 0.65;
    }

    internal static bool LooksLikeCharacterDialogue(string text)
    {
        if (!LooksLikeEnglishSubtitle(text)) return false;
        if (Regex.IsMatch(text, @"\b(?:I|me|my|mine|we|us|our|ours|you|your|yours)\b", RegexOptions.IgnoreCase) ||
            text.Contains('?')) return true;
        // Persistent action buttons and quest imperatives inside an imperfect ROI
        // may need translation, but must not silence teaching or microphone invites.
        return !Regex.IsMatch(text, @"^(?:Head|Go|Leave|Return|Talk|Search|Defeat|Collect|Equip|Click|Press|Tap|Hold|Use|Upgrade|Enter|Follow|Open|Close|Claim|Select|Choose)\b", RegexOptions.IgnoreCase) &&
            WordRegex().Matches(text).Count >= 5;
    }

    internal static bool IsChatOrAdvertising(string text)
    {
        if (Regex.IsMatch(text, @"https?\s*:|www\s*\.|\burl\s*[:>]|\b[\w-]+\.(?:com|top|net|gg)\b|\b(?:discount|cheap|consultation|large\s+quantity)\b", RegexOptions.IgnoreCase))
            return true;
        // Diablo chat usually begins with a bracketed channel number/name. Drop
        // those lines before translation; a speaker such as "Cain:" remains.
        if (Regex.IsMatch(text, @"^\s*\[(?:\s*\d+\s*|world|zone|trade|party|clan)\]", RegexOptions.IgnoreCase))
            return true;
        // Catch cropped trade messages only when a commerce word and a currency
        // or buy/sell marker occur together. Do not discard story uses of buy.
        return Regex.IsMatch(text, @"\b(?:WTS|WTB|selling|buying|eternal\s*orbs?|platinum)\b", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text, @"[$＄€£]|\b(?:price|USD|TWD|NTD|cheap|discount|orbs?|platinum|WTS|WTB)\b", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[^A-Za-z0-9]+|[^A-Za-z0-9.!?'\-]+$")]
    private static partial Regex DecorativeRegex();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*")]
    private static partial Regex WordRegex();
}
