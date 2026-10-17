using System.Diagnostics;
using System.Text.Json;
using Windows.Media.Ocr;

namespace DiabloEnglishCoach;

// Synthetic fixtures only: no screen capture, microphone, or online service.
internal static class OcrBenchmark
{
    public static async Task RunAsync(string output)
    {
        var samples = new[] {
            "Follow Deckard Cain to Ashwold Cemetery.",
            "Your summons deal 20% more damage.",
            "Do not open the gate until I return.",
            "I need your help. Follow me and stay close.",
            "The skill is not ready yet. Wait for the cooldown."
        };
        var ocr = new OcrService();
        var runs = new List<object>();
        for (var repeat = 0; repeat < 3; repeat++)
        foreach (var expected in samples)
        {
            using var bitmap = new Bitmap(1100, 95);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Arial", 24))
            {
                graphics.Clear(Color.FromArgb(25, 25, 25));
                graphics.DrawString(expected, font, Brushes.White, 15, 20);
            }
            foreach (var preprocess in new[] { false, true })
            {
                var watch = Stopwatch.StartNew();
                using var input = preprocess ? CaptureService.PrepareForOcr(bitmap) : (Bitmap)bitmap.Clone();
                try
                {
                    var text = await ocr.RecognizeAsync(input, CancellationToken.None, dialogue: true);
                    runs.Add(new { repeat, preprocess, expected, text, ms = watch.Elapsed.TotalMilliseconds,
                        exact = string.Equals(expected, text, StringComparison.Ordinal) });
                }
                catch (Exception ex)
                {
                    runs.Add(new { repeat, preprocess, expected, error = ex.Message,
                        hresult = ex.HResult, ms = watch.Elapsed.TotalMilliseconds, exact = false });
                }
                await Task.Delay(100);
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new {
            fixture = "Synthetic Arial 24pt; not a live game accuracy test",
            language = ocr.RecognizerLanguage,
            available = OcrEngine.AvailableRecognizerLanguages.Select(x => x.LanguageTag).ToArray(),
            runs
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
