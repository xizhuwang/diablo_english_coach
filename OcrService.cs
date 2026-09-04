using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DiabloEnglishCoach;

internal sealed partial class OcrService
{
    private readonly OcrEngine _engine;

    public string RecognizerLanguage { get; }

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

    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        using var randomAccess = new InMemoryRandomAccessStream();
        await memory.CopyToAsync(randomAccess.AsStreamForWrite(), cancellationToken);
        randomAccess.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccess).AsTask(cancellationToken);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken);
        var result = await _engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
        return Clean(result.Text);
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
        if (text.Length < 4)
            return false;

        var letters = text.Count(char.IsLetter);
        var latin = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        var words = WordRegex().Matches(text).Count;
        return words >= 2 && letters > 0 && latin / (double)letters >= 0.65;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[^A-Za-z0-9]+|[^A-Za-z0-9.!?'\-]+$")]
    private static partial Regex DecorativeRegex();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*")]
    private static partial Regex WordRegex();
}
